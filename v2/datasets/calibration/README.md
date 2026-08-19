# Offline calibration artifacts

The two `*-to-machine.json` files intentionally contain exact affine transforms for deterministic software tests.
They prove tuple order and frame direction only. They do **not** prove camera intrinsic calibration, lens distortion,
height robustness, or 0.05 mm equipment capability.

A commissioned calibration bundle must bind camera/lens serials, image size, work-plane Z, calibration target,
intrinsic parameters, distortion model, independent validation points, temperature, operator, and approval state.
Replace all three files before automatic motion is enabled.
