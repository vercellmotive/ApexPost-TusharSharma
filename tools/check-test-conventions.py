"""Check the test conventions in TESTING-STRATEGY.md section 7 for every xUnit test in the solution.

Naming: every test method is Method_Scenario_ExpectedResult (three parts; none is an extra "_And..." part).
AAA: every test has an optional `// Arrange`, then exactly one `// Act` and one `// Assert`, in that order, with
nothing before `// Assert` that asserts: no Assert.*, no Moq Verify, and no call to a helper method that asserts
(found automatically, for example ReadProblem or AssertProblemAsync).

Usage, from the repository root:   python3 tools/check-test-conventions.py [--list]
Exit code 0 when every test passes, 1 otherwise.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEST_PROJECTS = ["ApexPost.UnitTests", "ApexPost.IntegrationTests"]
HELPER_PROJECTS = TEST_PROJECTS + ["ApexPost.Testing", "ApexPost.Specs"]

TEST_ATTRIBUTE = re.compile(r"^\s*\[(\w*Fact|\w*Theory)\b")
METHOD = re.compile(r"^\s*(?:public|private|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\],\s?]+?\s+(\w+)\s*\(")
MARKER = re.compile(r"^\s*//\s*(Arrange|Act|Assert)\b(.*)$")
ASSERTS = re.compile(r"(?<![\w.])Assert\.|(?<![\w.])Assert[A-Z]\w*\s*\(|\.Verify(All|NoOtherCalls)?\s*\(")
THREE_PARTS = re.compile(r"^[A-Z][A-Za-z0-9]*_[A-Za-z0-9]+_[A-Za-z0-9]+$")


def source_files(projects):
    for project in projects:
        for directory, _, files in os.walk(os.path.join(ROOT, project)):
            if f"{os.sep}obj" in directory or f"{os.sep}bin" in directory:
                continue
            for name in sorted(files):
                if name.endswith(".cs") and not name.endswith(".feature.cs"):
                    path = os.path.join(directory, name)
                    yield path, open(path, encoding="utf-8").read().splitlines()


def body_end(lines, start):
    depth, opened = 0, False
    for index in range(start, len(lines)):
        code = re.sub(r'"(?:[^"\\]|\\.)*"', '""', lines[index]).split("//")[0]
        for character in code:
            if character == "{":
                depth, opened = depth + 1, True
            elif character == "}":
                depth -= 1
        if (opened and depth == 0) or (not opened and code.strip().endswith(";")):
            return index
    return len(lines) - 1


def is_test(lines, index):
    above = index - 1
    while above >= 0 and lines[above].strip().startswith("["):
        if TEST_ATTRIBUTE.match(lines[above]):
            return True
        above -= 1
    return False


def asserting_helpers():
    bodies = {}
    for _, lines in source_files(HELPER_PROJECTS):
        for index, line in enumerate(lines):
            match = METHOD.match(line)
            if not match or re.search(r"\b(class|record|struct|interface)\b", line) or is_test(lines, index):
                continue
            bodies.setdefault(match.group(1), []).append("\n".join(lines[index:body_end(lines, index) + 1]))
    helpers = {name for name, texts in bodies.items() if any(ASSERTS.search(text) for text in texts)}
    grew = True
    while grew:
        grew = False
        for name, texts in bodies.items():
            if name not in helpers and any(re.search(rf"\b{re.escape(h)}\s*\(", t) for t in texts for h in helpers):
                helpers.add(name)
                grew = True
    return helpers


def main():
    helpers = asserting_helpers()
    helper_call = re.compile(r"\b(" + "|".join(sorted(map(re.escape, helpers))) + r")\s*\(") if helpers else None
    total, problems = 0, []
    for path, lines in source_files(TEST_PROJECTS):
        for index, line in enumerate(lines):
            if not TEST_ATTRIBUTE.match(line):
                continue
            start = index + 1
            while not METHOD.match(lines[start]):
                start += 1
            name = METHOD.match(lines[start]).group(1)
            end = body_end(lines, start)
            total += 1
            reasons, markers, assert_seen = [], [], False
            if not THREE_PARTS.match(name) or any(part.startswith("And") for part in name.split("_")[1:]):
                reasons.append("name is not Method_Scenario_ExpectedResult")
            for number in range(start + 1, end + 1):
                text = lines[number].strip()
                marker = MARKER.match(lines[number])
                if marker:
                    markers.append(marker.group(1))
                    assert_seen = assert_seen or marker.group(1) == "Assert"
                    if marker.group(2).strip():
                        reasons.append(f"marker '// {marker.group(1)}{marker.group(2)}'")
                elif text and not text.startswith("//") and not assert_seen:
                    if ASSERTS.search(text):
                        reasons.append(f"asserts before // Assert (line {number + 1})")
                    elif helper_call and helper_call.search(text):
                        reasons.append(f"calls an asserting helper before // Assert (line {number + 1})")
            if markers not in (["Act", "Assert"], ["Arrange", "Act", "Assert"]):
                reasons.append("sections are " + ("/".join(markers) or "missing"))
            if reasons:
                problems.append((os.path.relpath(path, ROOT), start + 1, name, reasons))

    print(f"{total} tests checked, {len(problems)} break the naming or AAA rules.")
    print(f"Helpers treated as assertions (allowed only in // Assert): {', '.join(sorted(h for h in helpers if not h.startswith(('Then', 'Given'))))}")
    if "--list" in sys.argv or problems:
        for path, line, name, reasons in problems:
            print(f"{path}:{line} {name}: {'; '.join(reasons)}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
