"""Report merged coverage and enforce line and branch thresholds."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

report = ET.parse(sys.argv[1]).getroot()
valid = int(report.attrib["lines-valid"])
covered = int(report.attrib["lines-covered"])
if valid == 0:
    raise SystemExit("Coverage report contains no executable lines")
line = covered / valid * 100
branch_valid = int(report.attrib["branches-valid"])
branch_covered = int(report.attrib["branches-covered"])
if branch_valid == 0:
    raise SystemExit("Coverage report contains no branches")
branch = branch_covered / branch_valid * 100
methods = report.findall(".//class/methods/method")
covered_methods = sum(any(int(item.attrib.get("hits", 0)) > 0 for item in method.findall("./lines/line")) for method in methods)
method = covered_methods / len(methods) * 100 if methods else 0
summary = (f"## Coverage\n\nLine: {line:.2f}% ({covered}/{valid})\n\n"
           f"Branch: {branch:.2f}% ({branch_covered}/{branch_valid})\n\nMethod: {method:.2f}%\n\n"
           f"Required line and branch coverage: 85% — {'PASS' if line >= 85 and branch >= 85 else 'FAIL'}\n")
print(summary)
Path(sys.argv[2]).write_text(summary, encoding="utf-8")
if line < 85 or branch < 85:
    raise SystemExit(1)
