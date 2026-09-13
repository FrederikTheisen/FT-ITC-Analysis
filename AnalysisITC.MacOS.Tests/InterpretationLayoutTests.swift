// Layout-only AppKit fixture for the report actions and package footer.
// Reads the production button titles; no saved settings, application data or network.
// Run: xcrun swift AnalysisITC.MacOS.Tests/InterpretationLayoutTests.swift
import AppKit

let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
let source = try String(contentsOf: repository.appendingPathComponent(
    "AnalysisITC.MacOS/ViewControllers/AnalysisReportViewController.cs"), encoding: .utf8)
func button(_ name: String) -> NSButton {
    let pattern = #"readonly NSButton "# + name + #" = Button\("([^"]+)"\);"#
    let regex = try! NSRegularExpression(pattern: pattern)
    let match = regex.firstMatch(in: source, range: NSRange(source.startIndex..., in: source))!
    let title = String(source[Range(match.range(at: 1), in: source)!])
    let button = NSButton(title: title, target: nil, action: nil)
    button.bezelStyle = .rounded
    button.translatesAutoresizingMaskIntoConstraints = false
    return button
}

let app = NSApplication.shared
app.setActivationPolicy(.prohibited)
var failures: [String] = []
func expect(_ condition: Bool, _ message: String) {
    if !condition { failures.append(message) }
}
expect(source.contains("interpretationActions.Distribution = NSStackViewDistribution.Fill;"),
       "production report actions must not use equal widths")
expect(source.contains(#"SetAccessibilityLabel(editInterpretationButton, "Edit report interpretation")"#),
       "Edit must retain its descriptive accessibility label")
expect(source.contains(#"SetAccessibilityLabel(generateInterpretationButton, "Generate interpretation")"#),
       "Generate must expose its new accessibility label")
expect(source.contains(#"SetAccessibilityLabel(savePackage, "Save interpretation package locally without generation")"#),
       "Save package must expose its descriptive accessibility label")
expect(source.contains(#"readonly NSButton omitScientificGuidance = Button("Omit scientific guidance");"#),
       "administrator generation must define the non-persistent guidance omission control")
expect(source.contains("interpretationOptions.SupportsGuidanceOmission && !isSummary"),
       "guidance omission must be advertised by the server and hidden for Summary")
expect(source.contains("InterpretationAccessDisplay.GenerationProvenance(generated)"),
       "the generated draft must show model, reasoning and guidance provenance")

func checkRow(_ buttons: [NSButton], width: CGFloat, fontSize: CGFloat, compactFirst: Bool) {
    let host = NSView(frame: NSRect(x: 0, y: 0, width: width, height: 40))
    let row = NSStackView(views: buttons)
    row.orientation = .horizontal
    row.alignment = .centerY
    row.distribution = .fill
    row.spacing = 8
    row.translatesAutoresizingMaskIntoConstraints = false
    for button in buttons { button.font = .systemFont(ofSize: fontSize) }
    if compactFirst {
        buttons[0].setContentHuggingPriority(.init(999), for: .horizontal)
        buttons[1].setContentHuggingPriority(.init(249), for: .horizontal)
    }
    host.addSubview(row)
    NSLayoutConstraint.activate([
        row.leadingAnchor.constraint(equalTo: host.leadingAnchor),
        row.trailingAnchor.constraint(equalTo: host.trailingAnchor),
        row.topAnchor.constraint(equalTo: host.topAnchor),
    ])
    host.layoutSubtreeIfNeeded()
    expect(abs(host.frame.width - width) < 0.5, "row expanded its host")
    for (index, button) in buttons.enumerated() {
        let frame = button.alignmentRect(forFrame: button.frame)
        expect(button.frame.width >= button.intrinsicContentSize.width - 0.5,
               "\(button.title) is clipped at font \(fontSize)")
        expect(frame.minX >= -0.5 && frame.maxX <= width + 0.5,
               "\(button.title) extends beyond its row")
        if index > 0 {
            let previous = buttons[index - 1].alignmentRect(forFrame: buttons[index - 1].frame)
            expect(previous.maxX <= frame.minX, "buttons overlap")
        }
    }
    if compactFirst { expect(buttons[0].frame.width < buttons[1].frame.width, "Edit should be compact") }
}

for fontSize: CGFloat in [13, 18] {
    let edit = button("editInterpretationButton")
    let generate = button("generateInterpretationButton")
    expect(edit.title == "Edit" && generate.title == "Generate interpretation…", "incorrect report action titles")
    checkRow([edit, generate], width: 308, fontSize: fontSize, compactFirst: true)
    let save = button("savePackage")
    expect(save.title == "Save package", "package button must be short, without ellipsis")
    checkRow([save, button("cancel"), button("generate"), button("use")],
             width: 580, fontSize: fontSize, compactFirst: false)
}
if !failures.isEmpty {
    for failure in failures { print("FAIL: \(failure)") }
    exit(1)
}
print("PASS: Native interpretation actions and package footer fit their existing widths at normal and larger fonts.")
