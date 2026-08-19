# V2 deterministic offline dataset

`manifest.json` is the machine-readable entry point. All image paths in the manifest are relative to the manifest.
`sha256` contains separate board and pin hashes so a regression runner can reject changed inputs before executing.
`expectedFailureCodes` is the complete expected business-code set, not merely a subset. The nominal and bent cases
also contain the two deliberate motion-inhibit codes because both cameras are file sources and the recipe lifecycle
is `Engineering`. The missing-pin cycle exits before those downstream inhibit gates and therefore expects `PinMissing`
only.

The dataset contains one nominal PCB image, one nominal pin row, one missing-pin case, and one bent-pin case. All
four images live below `v2/datasets`, so V2 remains runnable when the legacy project is absent. PGM was chosen for
the pin images because it is a deterministic, lossless format that HALCON reads directly and that can be reviewed
without a proprietary encoder.

All cases are engineering-only. Exact synthetic calibration residuals must never be interpreted as equipment
accuracy. A production dataset needs real batches, CMM/metrology ground truth, lighting/height/temperature sweeps,
camera fault injection, and a locked qualification split.
