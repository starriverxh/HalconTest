# 视觉服务 Socket 协议

## 1. 传输

- TCP，默认地址为 `127.0.0.1:9100`。
- UTF-8，一行一个JSON对象（NDJSON），用LF结束，兼容CRLF。
- 每个请求带唯一 `requestId`，响应原样带回。
- 单连接可连续发送多条请求；实现已处理TCP拆包和粘包。
- 单条报文最大64 KiB；数值单位固定为mm和deg。

## 2. 请求格式

### ping

~~~json
{"requestId":"req-0001","command":"ping"}
~~~

### status

~~~json
{"requestId":"req-0002","command":"status"}
~~~

### trigger

~~~json
{"requestId":"req-0003","command":"trigger","timeoutMs":5000,"parameters":{"partId":"PCB-001","recipe":"pcb-pin-row-demo","toleranceMm":0.05}}
~~~

HALCON后端当前返回：

~~~json
{
  "requestId": "req-0003",
  "command": "trigger",
  "ok": true,
  "code": "OK",
  "data": {
    "isOk": true,
    "x": 0.018,
    "y": -0.012,
    "theta": 0.006,
    "score": 0.982,
    "resultPath": "runtime/results/..."
  }
}
~~~

`x/y/theta` 是机器坐标系下建议补偿量。PLC仍须检查软限位、碰撞区和安全状态。

### shutdown

~~~json
{"requestId":"req-0004","command":"shutdown"}
~~~

默认只允许回环地址关闭。生产中通常由Windows服务管理器管理进程。

## 3. 状态和错误码

`status` 返回ready/busy、后端就绪、启动时间、连接数、成功/失败次数和进程号。

| 错误码 | 含义 |
|---|---|
| OK | 请求成功 |
| INVALID_JSON | JSON或UTF-8错误 |
| INVALID_REQUEST | 缺requestId/command或字段非法 |
| UNKNOWN_COMMAND | 命令不存在 |
| BUSY | 另一视觉任务正在运行 |
| TIMEOUT | 视觉处理超时 |
| CANCELLED | 服务停止导致取消 |
| FORBIDDEN | 非本机请求关闭且未授权 |
| BACKEND_ERROR | HALCON/相机/算法执行异常 |
| INTERNAL_ERROR | 服务内部异常 |
| MESSAGE_TOO_LARGE | 报文超过64 KiB |

真实相机版本还应把BACKEND_ERROR细分为CAMERA_OFFLINE、ACQUISITION_TIMEOUT、TARGET_NOT_FOUND、LOW_SCORE、CALIBRATION_INVALID和CORRECTION_OUT_OF_RANGE等工艺错误。

## 4. 上位机规则

1. 连接失败有限次数重连，禁止无限快速重试。
2. 同一零件使用唯一requestId。
3. 只有 `ok=true`、`code=OK`、`data.isOk=true` 才能使用补偿。
4. 超时后不要盲目再次运动；先对账或转人工。
5. 把请求、响应、轴实际位置和最终质量结果放进同一追溯记录。
6. Socket不承担相机微秒级曝光同步，硬触发由PLC/运动控制器完成。

报文文件位于 `protocol/examples.ndjson`，纯C#调用位于 `src/Trigger.Client/Program.cs`。

