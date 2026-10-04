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
inspector.spacing = 5
inspector.translatesAutoresizingMaskIntoConstraints = false

// Mirrors AddFullWidthArrangedSubview: width alignment alone does not stretch arranged views.
func addFullWidth(_ view: NSView, to stack: NSStackView) {
    view.translatesAutoresizingMaskIntoConstraints = false
    stack.addArrangedSubview(view)
    view.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
}

// Storyboard Fit Summary section: separator and bold header directly in the inspector stack.
let summarySeparator = NSBox()
summarySeparator.boxType = .separator
addFullWidth(summarySeparator, to: inspector)
let summaryHeader = NSTextField(labelWithString: "Fit Summary")
summaryHeader.font = NSFont.boldSystemFont(ofSize: NSFont.systemFontSize)
addFullWidth(summaryHeader, to: inspector)

let section = NSStackView()
section.orientation = .vertical
section.alignment = .width
section.distribution = .fill
section.spacing = 5
section.translatesAutoresizingMaskIntoConstraints = false
let separator = NSBox()
separator.boxType = .separator
addFullWidth(separator, to: section)
let header = NSTextField(labelWithString: "Null hypothesis test")
header.font = NSFont.boldSystemFont(ofSize: NSFont.systemFontSize)
addFullWidth(header, to: section)
let rows = NSStackView()
rows.orientation = .vertical
rows.alignment = .width
rows.distribution = .fill
rows.spacing = 2
addFullWidth(rows, to: section)

func addRow(_ name: String, _ value: String) -> NSTextField {
    let row = NSStackView()
    row.orientation = .horizontal
    row.alignment = .firstBaseline
    row.distribution = .fill
    row.spacing = 8
    row.translatesAutoresizingMaskIntoConstraints = false
    let key = NSTextField(labelWithString: name)
    key.font = NSFont.systemFont(ofSize: NSFont.smallSystemFontSize)
    key.widthAnchor.constraint(equalToConstant: 72).isActive = true
    key.setContentHuggingPriority(NSLayoutConstraint.Priority(750), for: .horizontal)
    let result = NSTextField(labelWithString: value)
    result.font = NSFont.systemFont(ofSize: NSFont.smallSystemFontSize)
    result.alignment = .right
    result.lineBreakMode = .byWordWrapping
    result.cell?.wraps = true
    result.cell?.usesSingleLineMode = false
    result.maximumNumberOfLines = 0
    result.setContentHuggingPriority(NSLayoutConstraint.Priority(249), for: .horizontal)
    result.setContentCompressionResistancePriority(NSLayoutConstraint.Priority(250), for: .horizontal)
    row.addArrangedSubview(key)
    row.addArrangedSubview(result)
    addFullWidth(row, to: rows)
    return result
}

_ = addRow("Model", "Offset")
_ = addRow("Null RMSD", "4.184")
let numeric = addRow("ΔAICc", "+10")
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
if section.arrangedSubviews.count != 3 { failures.append("Expected separator, heading and row stack") }
if rows.arrangedSubviews.count != 4 { failures.append("Expected exactly four null test rows") }
if numeric.toolTip?.contains("private-id") == true { failures.append("Tooltip exposed an experiment ID") }
if conclusion.frame.width < 170 { failures.append("Conclusion value did not receive the available row width") }
if conclusion.frame.height < 12 { failures.append("Conclusion value has no laid out height (\(conclusion.frame.height))") }
if section.frame.width != 280 { failures.append("Section did not fill the inspector's 280 pt content width") }
func frameInRoot(_ view: NSView) -> NSRect { view.convert(view.bounds, to: root) }
let summaryHeaderFrame = frameInRoot(summaryHeader)
let headerFrame = frameInRoot(header)
if headerFrame.minX != summaryHeaderFrame.minX || headerFrame.width != summaryHeaderFrame.width {
    failures.append("Null header \(headerFrame) is not aligned with the Fit Summary header \(summaryHeaderFrame)")
}
if frameInRoot(separator).minX != frameInRoot(summarySeparator).minX
    || frameInRoot(separator).size != frameInRoot(summarySeparator).size {
    failures.append("Null separator does not match the Fit Summary separator")
}
let summaryGap = frameInRoot(summarySeparator).minY - summaryHeaderFrame.maxY
let nullGap = frameInRoot(separator).minY - headerFrame.maxY
if summaryGap != nullGap { failures.append("Separator-to-header gap \(nullGap) differs from Fit Summary \(summaryGap)") }
let repoRoot = URL(fileURLWithPath: #filePath)
    .deletingLastPathComponent().deletingLastPathComponent()
let sourceURL = repoRoot
    .appendingPathComponent("AnalysisITC.MacOS/ViewControllers/MainViews/DataAnalysisViewController.cs")
let productionSource = (try? String(contentsOf: sourceURL, encoding: .utf8)) ?? ""
for expected in ["NullModelComparisonPresentation.AnalysisInspectorTitle",
                 "NullModelComparisonPresentation.AnalysisInspectorRows(",
                 "BoxType = NSBoxType.NSBoxSeparator",
                 "NSFont.BoldSystemFontOfSize(NSFont.SystemFontSize)",
                 "AddFullWidthArrangedSubview(nullHypothesisTestStack, header)",
                 "AddFullWidthArrangedSubview(nullHypothesisTestStack, separator)",
                 "AddFullWidthArrangedSubview(nullTestRowsStack, NullTestRow(row))",
                 "Alignment = NSLayoutAttribute.Width",
                 "FitSummaryView?.Superview is not NSStackView inspectorStack",
                 "inspectorStack.AddArrangedSubview(nullHypothesisTestStack)"] {
    if !productionSource.contains(expected) { failures.append("Production inspector source does not contain: \(expected)") }
}

if productionSource.contains("RMSD / ΔAICc") {
    failures.append("Live inspector still uses the combined RMSD / ΔAICc row")
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
