# Socket 协议与运行状态

## 协议约定

- TCP + UTF-8；
- 一条消息一行 JSON（NDJSON），以 `\n` 结束；
- 当前 `protocolVersion = "2.0"`；
- 单行最大 64 KiB；
- `requestId` 长度 1～128，只允许字母、数字、`.`、`-`、`_`、`:`；
- 同一个 `requestId` + 相同内容重发，返回原结果并设置 `isReplay=true`；
- 同一个 `requestId` + 不同内容，返回 `REQUEST_ID_CONFLICT`；
- 数值单位固定为 mm 和 deg，不允许客户端猜单位。

## 两个端口

| 端口 | 命令 | 安全限制 |
|---|---|---|
| Production 9200 | `hello/status/inspect/ack/reset_fault` | `inspect` 不接收本地图片路径；未调试完成时拒绝 |
| Engineering 9201 | `hello/status/engineering.inspect_offline/ack/reset_fault` | 只监听回环地址；结果不授权运动 |

正式部署可把 Production 绑定到指定设备网卡；Engineering 应保持本机回环并结合用户权限。

## 命令信封

线上的 JSON 是一行，下面为便于阅读做了换行：

```json
{
  "protocolVersion": "2.0",
  "requestId": "LINE1-20260819-000001",
  "command": "inspect",
  "payload": {
    "productId": "PCB-A",
    "recipeId": "pcb-a-2.1",
    "serialNumber": "SN000123"
  }
}
```

生产上位机不需要知道 HALCON 算子。它只负责：连接、发送带唯一 `requestId` 的触发、等待结果、校验同一 ID、执行或拒绝动作、发送 `ack`。

当前协议是同步请求/最终结果：`inspect` 或 `engineering.inspect_offline` 在算法周期完成后直接返回最终 JSON，
不会先返回一个“已接单 ACK”。下文的 `ack` 是客户端确认“最终结果已被消费”，两者不要混淆。

## 离线工程触发

```json
{
  "protocolVersion": "2.0",
  "requestId": "learn-001",
  "command": "engineering.inspect_offline",
  "payload": {
    "productId": "TRAINING-PCB",
    "recipeId": "pcb-pin-row-offline-v2",
    "boardImagePath": null,
    "pinImagePath": null
  }
}
```

路径为 `null` 时读取配方内的默认离线图。即使结果几何通过，响应仍应是：

```text
disposition  = EngineeringOnly
motionAllowed = false
```

## 结果响应

```json
{
  "protocolVersion": "2.0",
  "requestId": "learn-001",
  "success": true,
  "status": "ENGINEERINGONLY",
  "payload": {
    "requestId": "learn-001",
    "productId": "TRAINING-PCB",
    "recipeId": "pcb-pin-row-offline-v2",
    "recipeVersion": 1,
    "disposition": "EngineeringOnly",
    "measurementValid": true,
    "motionAllowed": false,
    "commandXmm": 0.12,
    "commandYmm": -0.08,
    "commandThetaDeg": 0.03,
    "alignmentRmseMm": 0.004,
    "maximumPinResidualMm": 0.007,
    "pointResiduals": [],
    "failures": [],
    "stateHistory": [],
    "durationMs": 85,
    "resultPath": "..."
  },
  "isReplay": false
}
```

`success=true` 只说明服务正常完成命令，不等于 `MotionAllowed=true`。设备必须显式检查 `motionAllowed`，不得用 `status` 文本或 `commandXmm != 0` 代替。

## 结果确认

设备收到并消费结果后发送：

```json
{
  "protocolVersion": "2.0",
  "requestId": "LINE1-20260819-000001-ack",
  "command": "ack",
  "payload": {
    "completedRequestId": "LINE1-20260819-000001"
  }
}
```

当前实现按单工位只保留一个待确认结果，并严格校验 `completedRequestId` 与待确认周期一致；不一致返回
`ACK_REQUEST_ID_MISMATCH`，且工位继续停留在 `ResultAvailable`，不会误放行下一周期。

## 状态机

```text
Starting
  ├─ 硬件/标定未完成 → Uncommissioned
  └─ 自检和发布快照通过 → Ready

Ready/Uncommissioned(仅工程)
  → Processing
  → ResultAvailable
  → ACK
  → Ready/Uncommissioned

Processing → 系统故障 → Faulted
Faulted → reset_fault + 重新自检 → Ready/Uncommissioned
```

产品 NG 仍进入 `ResultAvailable`，而不是 `Faulted`。

周期内部的最后一个 `stateHistory` 节点用于区分四种结果：`Completed`（合格且可授权）、
`Inhibited`（测量有效但工程/安全条件禁止运动）、`Rejected`（产品或测量 NG）、`Faulted`（系统故障）。

## 常见协议错误

| Code | 含义 | 客户端动作 |
|---|---|---|
| `INVALID_JSON` | JSON 无法解析 | 修正报文，不重发原错误内容 |
| `PROTOCOL_VERSION_UNSUPPORTED` | 协议版本不兼容 | 停机并升级/切换版本 |
| `INVALID_REQUEST_ID` | ID 格式不合法 | 生成新合法 ID |
| `REQUEST_ID_CONFLICT` | 相同 ID 内容不同 | 不能重用 ID，人工排查 |
| `BUSY` | 队列已满 | 按节拍退避后重试同一 ID |
| `STATION_UNAVAILABLE` | 未确认结果或状态不允许 | 查询 `status`，先 ACK/恢复 |
| `ACK_REQUEST_ID_MISMATCH` | ACK 对错了周期 | 保持停机，按 `lastCompletedRequestId` 排查 |
| `STATION_UNCOMMISSIONED` | 真实硬件链未验收 | 禁止运动，只能工程调试 |
| `EXECUTION_FAILED` | 周期执行异常 | 查询结果/日志，按 retryable 决定 |

## C# 上位机参考

完整可运行代码在 `src/Vision.Trigger.Client/Program.cs`。核心只有：

```csharp
using var client = new TcpClient();
await client.ConnectAsync("127.0.0.1", 9200);
await stream.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"));
var response = await reader.ReadLineAsync();
```

量产客户端还必须实现连接超时、读取超时、断线重连、requestId 持久化、响应 ID 对账和日志，不要无限自动生成新 ID 重试，否则可能重复生产动作。
