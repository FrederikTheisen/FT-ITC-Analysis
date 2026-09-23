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
    @objc var RegisterInterpretationButton: NSButton!
    @objc var VerifyInterpretationAccessButton: NSButton!
}
@objc(MacProcessingPreferencesViewController)
class ProcessingPane: NSViewController {
    @objc var DilutionPopup: NSPopUpButton!
    @objc var DilutionDescription: NSTextField!
}
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
let processing = tabs.tabViewItems[1].viewController as! ProcessingPane
processing.DilutionDescription.cell!.wraps = true
processing.DilutionDescription.cell!.usesSingleLineMode = false
processing.DilutionDescription.lineBreakMode = .byWordWrapping
processing.DilutionDescription.setContentCompressionResistancePriority(.init(250), for: .horizontal)
let code = general.InterpretationOperatorCodeField!
let status = general.InterpretationAccessLabel!
let details = general.InterpretationAccessDetailsLabel!
let model = general.InterpretationModelPopup!
let register = general.RegisterInterpretationButton!
let modelLabel = model.superview!.subviews.first { $0 is NSTextField } as! NSTextField
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
details.constraints.first { $0.firstAttribute == .height }!.constant = 16
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
details.isHidden = true
window.orderFront(nil)
settleLayout()
let initialWidth = window.frame.width

func descendants(_ view: NSView) -> [NSView] {
    view.subviews.flatMap { [$0] + descendants($0) }
}
let heading = descendants(general.view).compactMap { $0 as? NSTextField }
    .first { $0.stringValue == "Automated interpretation access" }
for item in tabs.tabViewItems {
    let pane = item.viewController!.view
    let scroll = descendants(pane).compactMap { $0 as? NSScrollView }.first
    let footer = pane.subviews.first { !($0 is NSScrollView) }
    let separator = footer.flatMap { descendants($0).compactMap { $0 as? NSBox }.first { $0.boxType == .separator } }
    expect(scroll != nil && separator != nil, "\(item.label): preferences scroll or footer separator is missing")
    if let scroll = scroll, let separator = separator {
        let scrollFrame = scroll.convert(scroll.bounds, to: pane)
        let separatorFrame = separator.convert(separator.bounds, to: pane)
        expect(abs(scrollFrame.minY - separatorFrame.midY) <= 0.5,
               "\(item.label): scroll view must extend to the footer separator")
    }
}
let processingLabels = descendants(tabs.tabViewItems[1].viewController!.view).compactMap { $0 as? NSTextField }
expect(processingLabels.contains { $0.stringValue == "Injection bookkeeping" }, "injection bookkeeping label is missing")
expect(!processingLabels.contains { $0.stringValue == "Dilution method" }, "obsolete dilution-method label remains")
let bookkeeping = descendants(tabs.tabViewItems[1].viewController!.view).compactMap { $0 as? NSPopUpButton }
    .first { $0.itemTitles == ["MicroCal", "Ideal continuous mixing", "Discrete displacement"] }
expect(bookkeeping != nil, "the three bookkeeping choices are missing")
let bookkeepingDescription: NSTextField? = processing.DilutionDescription
expect(bookkeepingDescription != nil && !bookkeepingDescription!.stringValue.isEmpty,
       "injection bookkeeping guidance row is missing")
if let bookkeeping = bookkeeping {
    for title in bookkeeping.itemTitles {
        bookkeeping.selectItem(withTitle: title)
        expect(bookkeeping.intrinsicContentSize.width <= bookkeeping.frame.width,
               "bookkeeping choice \(title) is truncated")
    }
    if let description = bookkeepingDescription {
        let selectorFrame = bookkeeping.convert(bookkeeping.bounds, to: bookkeeping.superview?.superview)
        let descriptionFrame = description.convert(description.bounds, to: bookkeeping.superview?.superview)
        expect(descriptionFrame.minY < selectorFrame.minY, "bookkeeping guidance must appear beneath the selector")
        expect(description.alignmentRect(forFrame: description.frame).width <= bookkeeping.superview!.superview!.frame.width + 0.5,
               "bookkeeping guidance must fit the processing content width")
    }
}
expect(heading != nil, "automated interpretation heading is missing")
expect(register.title == "Register for Automated Interpretation…", "registration button is missing or has the wrong title")
expect(abs(initialWidth - 500) < 0.5, "preferences must retain their 500-point width")

func checkWidth(_ stage: String) {
    settleLayout()
    expect(abs(window.frame.width - initialWidth) < 0.5,
           "\(stage): window grew from \(initialWidth) to \(window.frame.width) points")
    if let heading = heading {
        expect(heading.intrinsicContentSize.width <= heading.frame.width,
               "\(stage): automated interpretation heading is truncated")
    }
    expect(abs(code.frame.width - 240) < 0.5, "\(stage): code field is not 240 points wide")
    // AppKit includes extra bezel/shadow insets in a popup's frame, outside its layout width.
    let modelWidth = model.alignmentRect(forFrame: model.frame).width
    let codeWidth = code.alignmentRect(forFrame: code.frame).width
    expect(abs(modelWidth - codeWidth) < 0.5, "\(stage): code and dropdown layout widths differ")
    let verifyFrame = general.VerifyInterpretationAccessButton.convert(general.VerifyInterpretationAccessButton.bounds, to: stack)
    let codeFrame = code.convert(code.bounds, to: stack)
    expect(verifyFrame.maxX <= codeFrame.minX, "\(stage): verify button is not left of the code field")
    let registerFrame = register.convert(register.bounds, to: general.view)
    expect(registerFrame.maxX <= general.view.bounds.maxX + 0.5, "\(stage): registration button overflows the preferences pane")
    let detailsFrame = details.convert(details.bounds, to: general.view)
    expect(detailsFrame.maxX <= general.view.bounds.maxX + 0.5, "\(stage): account details overflow the preferences pane")
    if !model.superview!.isHidden {
        let row = model.superview as! NSStackView
        expect(row.orientation == .horizontal && row.alignment == .firstBaseline,
               "\(stage): preset label and dropdown must share a baseline")
        expect(modelLabel.intrinsicContentSize.width <= modelLabel.frame.width, "\(stage): preset label is truncated")
        let labelFrame = modelLabel.convert(modelLabel.bounds, to: general.view)
        let popupFrame = model.convert(model.bounds, to: general.view)
        expect(labelFrame.maxX <= popupFrame.minX, "\(stage): preset label must be left of the dropdown")
        expect(labelFrame.minY < popupFrame.maxY && popupFrame.minY < labelFrame.maxY,
               "\(stage): preset label and dropdown must be on the same line")
    }
}

func setAccountDetails(_ text: String) {
    details.stringValue = text
    details.isHidden = text.isEmpty
    guard !text.isEmpty else { return }
    let size = details.cell!.cellSize(forBounds: NSRect(x: 0, y: 0, width: max(1, details.bounds.width), height: 10_000))
    details.constraints.first { $0.firstAttribute == .height }!.constant = max(16, ceil(size.height))
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
setAccountDetails("Name:\t\tExample Account (Custom)\nEmail:\t\ttest@example.org (Example Institute)\nExpiry:\t\t10/1/2026\nQuota:\t\t82% remaining · resets 10/31/2026")
checkWidth("account loaded")
modelLabel.stringValue = "Interpretation depth"
model.superview!.isHidden = false
checkWidth("verified preset row revealed")
model.addItem(withTitle: String(repeating: "Long model name ", count: 20))
model.selectItem(at: model.numberOfItems - 1)
modelLabel.stringValue = "Model"
reasoning.superview!.isHidden = false
checkWidth("custom reasoning row revealed")
guidance.addItem(withTitle: String(repeating: "Long guidance name ", count: 20))
guidanceRow.isHidden = false
checkWidth("custom guidance row revealed")
status.stringValue = "Access: Verified (cached) · Checked: 11 September 2026, 15:30"
setAccountDetails("Name:\t\t" + String(repeating: "LongAccountName", count: 40) + " (Custom)\nEmail:\t\t" + String(repeating: "long", count: 40) + "@example.org (Very Long Example Institute)")
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
