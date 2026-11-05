import datetime
import json
import os
import sys

connection, functionality, vulnerability = sys.argv[1:]
states = {
    "Connection": (connection, "ConnectTimeout"),
    "Functionality": (functionality, "FunctionalCheckFailed"),
    "Vulnerability": (
        vulnerability,
        "VulnerabilityPresent" if functionality == "Passed" else "FunctionalCheckFailed",
    ),
}
required = json.loads(os.environ["NOCTF_CHECK_REQUIREMENTS"])
if not required or len(required) != len(set(required)):
    raise ValueError("检查要求必须非空且不得重复")
probes = []
for kind in required:
    result, reason = states[kind]
    if result not in ("Passed", "TargetFailure"):
        raise ValueError("探针结果无效")
    probes.append({"kind": kind, "result": result, "reason": "None" if result == "Passed" else reason})
print(json.dumps({"observedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(), "probes": probes}))
