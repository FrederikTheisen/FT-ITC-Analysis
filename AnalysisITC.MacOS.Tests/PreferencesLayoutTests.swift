// Native AppKit regression test; requires macOS and Xcode, not Xamarin or a server.
// From the repository root: xcrun swift AnalysisITC.MacOS.Tests/PreferencesLayoutTests.swift
// An optional argument selects another Preferences.storyboard for before/after checks.
import AppKit

// Stub application controllers so the real storyboard can load without settings,
// saved access codes, or network requests. Only layout-relevant outlets are needed.
@objc(MacPreferencesWindowController)
class PreferencesWindow: NSWindowController {}
@objc(MacGeneralPreferencesViewController)
class GeneralPane: NSViewController {
    @objc var InterpretationOperatorCodeField: NSSecureTextField!
    @objc var InterpretationAccessLabel: NSTextField!
    @objc var InterpretationAccessDetailsLabel: NSTextField!
    @objc var InterpretationModelPopup: NSPopUpButton!
    @objc var InterpretationReasoningPopup: NSPopUpButton!
    @objc var VerifyInterpretationAccessButton: NSButton!
}
@objc(MacProcessingPreferencesViewController)
class ProcessingPane: NSViewController {}
@objc(MacFittingPreferencesViewController)
class FittingPane: NSViewController {}
@objc(MacExportPreferencesViewController)
class ExportPane: NSViewController {}
@objc(FlippedPreferencesDocumentView)
class FlippedDocument: NSView { override var isFlipped: Bool { true } }

let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
let storyboardURL = CommandLine.arguments.count > 1
    ? URL(fileURLWithPath: CommandLine.arguments[1])
    : repository.appendingPathComponent("AnalysisITC.MacOS/Preferences.storyboard")
let files = FileManager.default
let temporary = files.temporaryDirectory.appendingPathComponent("ftitc-preferences-test-\(UUID().uuidString)")
let bundleURL = temporary.appendingPathComponent("Fixture.bundle")
let resources = bundleURL.appendingPathComponent("Contents/Resources")
try files.createDirectory(at: resources, withIntermediateDirectories: true)
defer { try? files.removeItem(at: temporary) }
let bundleInfo = ["CFBundleIdentifier": "org.ft-itc.preferences-layout-test", "CFBundleName": "Layout Test"]
try PropertyListSerialization.data(fromPropertyList: bundleInfo, format: .xml, options: 0)
    .write(to: bundleURL.appendingPathComponent("Contents/Info.plist"))

let compile = Process()
compile.executableURL = URL(fileURLWithPath: "/usr/bin/xcrun")
compile.arguments = ["ibtool", "--compile", resources.appendingPathComponent("Preferences.storyboardc").path, storyboardURL.path]
let diagnostics = Pipe()
compile.standardOutput = diagnostics
compile.standardError = diagnostics
try compile.run()
let compileOutput = diagnostics.fileHandleForReading.readDataToEndOfFile()
compile.waitUntilExit()
guard compile.terminationStatus == 0 else {
    FileHandle.standardError.write(compileOutput)
    exit(1)
}

let app = NSApplication.shared
app.setActivationPolicy(.prohibited)
let storyboard = NSStoryboard(name: "Preferences", bundle: Bundle(url: bundleURL)!)
let controller = storyboard.instantiateInitialController() as! PreferencesWindow
let window = controller.window!
let tabs = window.contentViewController as! NSTabViewController
for item in tabs.tabViewItems { _ = item.viewController!.view }
let general = tabs.tabViewItems[0].viewController as! GeneralPane
let code = general.InterpretationOperatorCodeField!
let status = general.InterpretationAccessLabel!
let details = general.InterpretationAccessDetailsLabel!
let model = general.InterpretationModelPopup!
let reasoning = general.InterpretationReasoningPopup!
let stack = reasoning.superview!.superview as! NSStackView

// Mirror the layout-only setup in MacGeneralPreferencesViewController.ViewDidLoad
// and CreateInterpretationGuidanceControl; all remaining constraints come from IB.
code.isHorizontalContentSizeConstraintActive = false
for field in [code, status, details] {
    field.setContentCompressionResistancePriority(.init(250), for: .horizontal)
}
details.cell!.wraps = true
details.cell!.usesSingleLineMode = false
details.constraints.first { $0.firstAttribute == .height }!.constant = 96
let guidanceLabel = NSTextField(labelWithString: "Scientific guidance")
guidanceLabel.translatesAutoresizingMaskIntoConstraints = false
guidanceLabel.setContentHuggingPriority(.init(249), for: .horizontal)
let guidance = NSPopUpButton(frame: .zero, pullsDown: false)
guidance.translatesAutoresizingMaskIntoConstraints = false
guidance.widthAnchor.constraint(equalToConstant: 240).isActive = true
let guidanceRow = NSStackView(views: [guidanceLabel, guidance])
guidanceRow.orientation = .horizontal
guidanceRow.alignment = .firstBaseline
guidanceRow.distribution = .fill
guidanceRow.spacing = 10
guidanceRow.translatesAutoresizingMaskIntoConstraints = false
stack.addArrangedSubview(guidanceRow)

func settleLayout() {
    window.contentView!.layoutSubtreeIfNeeded()
    for _ in 0..<5 { RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.02)) }
}

var failures: [String] = []
func expect(_ condition: Bool, _ message: String) {
    if !condition { failures.append(message) }
}
model.superview!.isHidden = true
reasoning.superview!.isHidden = true
guidanceRow.isHidden = true
code.stringValue = ""
status.stringValue = "Access: Not verified"
details.stringValue = "Verify your code to view account details."
window.orderFront(nil)
settleLayout()
let initialWidth = window.frame.width

func checkWidth(_ stage: String) {
    settleLayout()
    expect(abs(window.frame.width - initialWidth) < 0.5,
           "\(stage): window grew from \(initialWidth) to \(window.frame.width) points")
    expect(abs(code.frame.width - 240) < 0.5, "\(stage): code field is not 240 points wide")
    // AppKit includes extra bezel/shadow insets in a popup's frame, outside its layout width.
    let modelWidth = model.alignmentRect(forFrame: model.frame).width
    let codeWidth = code.alignmentRect(forFrame: code.frame).width
    expect(abs(modelWidth - codeWidth) < 0.5, "\(stage): code and dropdown layout widths differ")
    let verifyFrame = general.VerifyInterpretationAccessButton.convert(general.VerifyInterpretationAccessButton.bounds, to: stack)
    let codeFrame = code.convert(code.bounds, to: stack)
    expect(verifyFrame.maxX <= codeFrame.minX, "\(stage): verify button is not left of the code field")
}

code.stringValue = String(repeating: "x", count: 160)
checkWidth("code entered")
status.stringValue = "Access: Verifying…"
checkWidth("verifying")
model.removeAllItems()
model.addItems(withTitles: ["Fast", "Default", "Advanced", "Comprehensive"])
model.selectItem(withTitle: "Default")
checkWidth("presets populated")
status.stringValue = "Access: Verified"
details.stringValue = "Name: Example Account\nEmail: test@example.org\nAccess level: Custom · Expires: No expiry · Request limit: Unlimited\nUsage: 18 requests · Reset: Monthly\nMost recent request: 11 September 2026 · Status: Completed"
checkWidth("account loaded")
(model.superview!.subviews.first { $0 is NSTextField } as! NSTextField).stringValue = "Interpretation depth"
model.superview!.isHidden = false
checkWidth("verified preset row revealed")
model.addItem(withTitle: String(repeating: "Long model name ", count: 20))
model.selectItem(at: model.numberOfItems - 1)
reasoning.superview!.isHidden = false
checkWidth("custom reasoning row revealed")
guidance.addItem(withTitle: String(repeating: "Long guidance name ", count: 20))
guidanceRow.isHidden = false
checkWidth("custom guidance row revealed")
status.stringValue = "Access: Verified (cached) · Checked: 11 September 2026, 15:30"
details.stringValue = "Name: " + String(repeating: "LongAccountName", count: 40) + "\nEmail: " + String(repeating: "long", count: 40) + "@example.org"
checkWidth("long cached account text")
for index in [1, 2, 3, 0] {
    tabs.selectedTabViewItemIndex = index
    checkWidth("tab \(index)")
}
model.superview!.isHidden = true
reasoning.superview!.isHidden = true
guidanceRow.isHidden = true
status.stringValue = "Access: Not verified · " + String(repeating: "Long verification error. ", count: 20)
checkWidth("verification failed")
window.orderOut(nil)
if !failures.isEmpty {
    for failure in failures { print("FAIL: \(failure)") }
    exit(1)
}
print("PASS: Preferences width stayed at \(initialWidth) points through code entry, verification, preset/custom access, long account/error text, and tab changes.")
