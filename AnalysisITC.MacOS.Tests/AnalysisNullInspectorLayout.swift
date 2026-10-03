// Layout regression fixture for the compact native analysis inspector.
// Run from the repository root with: xcrun swift AnalysisITC.MacOS.Tests/AnalysisNullInspectorLayout.swift
import AppKit

let app = NSApplication.shared
app.setActivationPolicy(.prohibited)

let window = NSWindow(
    contentRect: NSRect(x: 0, y: 0, width: 300, height: 180),
    styleMask: [.titled], backing: .buffered, defer: false)
let root = NSView()
root.translatesAutoresizingMaskIntoConstraints = false
window.contentView = root
let inspector = NSStackView()
inspector.orientation = .vertical
inspector.alignment = .leading
inspector.distribution = .fill
inspector.spacing = 4
inspector.translatesAutoresizingMaskIntoConstraints = false
let summary = NSTextField(labelWithString: "Fit summary")
inspector.addArrangedSubview(summary)

let section = NSStackView()
section.orientation = .vertical
section.alignment = .width
section.distribution = .fill
section.spacing = 2
section.translatesAutoresizingMaskIntoConstraints = false
section.addArrangedSubview(NSTextField(labelWithString: "Null hypothesis test"))

func addRow(_ name: String, _ value: String) -> NSTextField {
    let row = NSStackView()
    row.orientation = .horizontal
    row.alignment = .firstBaseline
    row.distribution = .fill
    row.spacing = 4
    row.translatesAutoresizingMaskIntoConstraints = false
    let key = NSTextField(labelWithString: name)
    key.widthAnchor.constraint(equalToConstant: 92).isActive = true
    key.setContentHuggingPriority(NSLayoutConstraint.Priority(750), for: .horizontal)
    let result = NSTextField(labelWithString: value)
    result.lineBreakMode = .byWordWrapping
    result.cell?.wraps = true
    result.cell?.usesSingleLineMode = false
    result.maximumNumberOfLines = 0
    result.setContentHuggingPriority(NSLayoutConstraint.Priority(249), for: .horizontal)
    result.setContentCompressionResistancePriority(NSLayoutConstraint.Priority(250), for: .horizontal)
    row.addArrangedSubview(key)
    row.addArrangedSubview(result)
    section.addArrangedSubview(row)
    return result
}

_ = addRow("Model", "Offset")
let numeric = addRow("RMSD / ΔAICc", "4.184 / +10")
let conclusion = addRow("Conclusion", "No binding detected")
numeric.toolTip = "Null AICc: 110. RMSD unit: µJ."
inspector.addArrangedSubview(section)
NSLayoutConstraint(
    item: section, attribute: .width, relatedBy: .equal,
    toItem: inspector, attribute: .width, multiplier: 1, constant: 0).isActive = true
root.addSubview(inspector)
inspector.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 10).isActive = true
inspector.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -10).isActive = true
inspector.topAnchor.constraint(equalTo: root.topAnchor, constant: 8).isActive = true
window.layoutIfNeeded()
root.layoutSubtreeIfNeeded()
inspector.layoutSubtreeIfNeeded()

var failures: [String] = []
if section.arrangedSubviews.count != 4 { failures.append("Expected heading and exactly three rows") }
if numeric.toolTip?.contains("private-id") == true { failures.append("Tooltip exposed an experiment ID") }
if conclusion.frame.width < 170 { failures.append("Conclusion value did not receive the available row width") }
if conclusion.frame.height < 15 { failures.append("Conclusion value has no laid out height") }
if section.frame.width != 280 { failures.append("Section did not fill the inspector's 280 pt content width") }
let repoRoot = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent()
let sourceURL = repoRoot
    .appendingPathComponent("AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs")
let productionSource = (try? String(contentsOf: sourceURL, encoding: .utf8)) ?? ""
for expected in ["Null hypothesis test", "AddNullTestRow(\"Model\")", "AddNullTestRow(\"RMSD / ΔAICc\")",
                 "AddNullTestRow(\"Conclusion\")", "Alignment = NSLayoutAttribute.Width",
                 "FitSummaryView?.Superview is not NSStackView inspectorStack",
                 "inspectorStack.AddArrangedSubview(nullHypothesisTestStack)"] {
    if !productionSource.contains(expected) { failures.append("Production inspector source does not contain: \(expected)") }
}

if productionSource.contains("footerStack.AddArrangedSubview(nullHypothesisTestStack)") {
    failures.append("Null assessment is still attached to the footer")
}

let resultControllerURL = repoRoot
    .appendingPathComponent("AnalysisITC.MacOS/ViewControllers/MainViews/AnalysisResultTabViewController.cs")
let resultControllerSource = (try? String(contentsOf: resultControllerURL, encoding: .utf8)) ?? ""
for expected in ["foreach (var member in result.MemberAssessments)",
                 "new NSMenuItem($\"{index} — {member.SolutionName}\", (EventHandler)null)",
                 "ΔAICc {delta} · {effective} ({mode})",
                 "MemberComparisonUnavailableReason(member.Comparison)"] {
    if !resultControllerSource.contains(expected) { failures.append("Result assessment menu source does not contain: \(expected)") }
}

let finalFigureURL = repoRoot
    .appendingPathComponent("AnalysisITC.MacOS/GraphViews/FinalFigureGraphView.cs")
let finalFigureSource = (try? String(contentsOf: finalFigureURL, encoding: .utf8)) ?? ""
for expected in ["CurrentFigureBindingOutputAllowed(data)",
                 "ResultOutputPolicy.IsMemberBindingOutputAllowed(",
                 "owner, solution, ResultOutputPurpose.Standard)"] {
    if !finalFigureSource.contains(expected) { failures.append("Final figure member policy source does not contain: \(expected)") }
}

if failures.isEmpty {
    print("PASS: compact null inspector fits the 280 pt native Fit inspector content width")
} else {
    for failure in failures { fputs("FAIL: \(failure)\n", stderr) }
    exit(1)
}
