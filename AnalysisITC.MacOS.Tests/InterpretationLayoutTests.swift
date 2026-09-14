// Layout-only AppKit fixtures for report actions and the scrolling generation sheet.
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
expect(source.contains("const double DefaultSheetWidth = 620;"),
       "interpretation sheet must have a bounded default width")
expect(source.contains("View.Window.SetContentSize(size);"),
       "interpretation sheet must apply its bounded size to the presented window")
expect(source.contains("var width = bodyScroll.ContentView.Bounds.Width"),
       "document sizing must use the visible viewport width")
expect(!source.contains("var viewport = bodyScroll.ContentSize"),
       "document sizing must not use the scrollable content width")
let sheetSource = source.components(separatedBy: "sealed class AnalysisInterpretationViewController").last!
expect(sheetSource.contains("rootWidthConstraint = View.WidthAnchor.ConstraintEqualToConstant(")
       && sheetSource.contains("rootWidthConstraint.Active = true"),
       "the sheet must constrain its width before AppKit measures its content")
expect(sheetSource.contains("content.LeadingAnchor.ConstraintEqualToAnchor(bodyDocument.LeadingAnchor, 20)"),
       "body controls need an explicit leading margin")
expect(sheetSource.contains("content.TrailingAnchor.ConstraintEqualToAnchor(bodyDocument.TrailingAnchor, -20)"),
       "body controls need an explicit trailing margin")
expect(!sheetSource.contains("content.EdgeInsets ="),
       "body stack insets must not conflict with its full-width child constraints")
expect(!sheetSource.contains("content.BottomAnchor.ConstraintEqualToAnchor(bodyDocument.BottomAnchor)"),
       "the scrolling document must permit content to retain its natural height")
expect(sheetSource.contains("HorizontalContentSizeConstraintActive = false"),
       "dynamic text must not supply an unbounded intrinsic width")

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

// A real NSWindow is essential here: testing isolated button rows did not catch
// AppKit expanding the entire sheet in response to long labels. This mirrors
// only the layout, with synthetic text and no service, settings or project data.
final class SheetDocument: NSView { override var isFlipped: Bool { true } }
final class SheetRoot: NSView {
    override func draw(_ dirtyRect: NSRect) {
        // Offscreen captures do not include the owning window's background.
        NSColor.windowBackgroundColor.setFill()
        dirtyRect.fill()
    }
}
func sheetLabel(_ value: String, lines: Int = 1) -> NSTextField {
    let field = NSTextField(labelWithString: value)
    field.translatesAutoresizingMaskIntoConstraints = false
    field.isHorizontalContentSizeConstraintActive = false
    field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
    field.lineBreakMode = lines == 1 ? .byTruncatingTail : .byWordWrapping
    field.maximumNumberOfLines = lines
    return field
}
func sheetStack(_ views: [NSView], vertical: Bool) -> NSStackView {
    let stack = NSStackView(views: views)
    stack.translatesAutoresizingMaskIntoConstraints = false
    stack.orientation = vertical ? .vertical : .horizontal
    stack.alignment = vertical ? .leading : .centerY
    stack.spacing = 8
    stack.distribution = .fill
    if vertical {
        for view in views { view.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true }
    }
    return stack
}
func sheetEditor(_ height: CGFloat) -> NSScrollView {
    let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 580, height: height))
    scroll.translatesAutoresizingMaskIntoConstraints = false
    scroll.hasVerticalScroller = true
    scroll.hasHorizontalScroller = false
    scroll.autohidesScrollers = true
    scroll.borderType = .bezelBorder
    scroll.heightAnchor.constraint(equalToConstant: height).isActive = true
    let text = NSTextView(frame: scroll.contentView.bounds)
    text.isHorizontallyResizable = false
    text.isVerticallyResizable = true
    text.autoresizingMask = .width
    text.textContainerInset = NSSize(width: 7, height: 5)
    text.textContainer?.widthTracksTextView = true
    scroll.documentView = text
    return scroll
}
func checkSheet(width: CGFloat) {
    let height: CGFloat = 620
    let root = SheetRoot(frame: NSRect(x: 0, y: 0, width: width, height: height))
    root.widthAnchor.constraint(equalToConstant: width).isActive = true
    let heading = sheetLabel("Generate interpretation")
    heading.font = .boldSystemFont(ofSize: 13)
    let notice = sheetLabel(String(repeating: "Selected evidence and context are sent to the interpretation service. ", count: 5), lines: 0)
    let context = sheetEditor(120)
    let status = sheetLabel("Service: Available", lines: 2)
    let account = sheetLabel("Account: Research group · Advanced", lines: 2)
    let model = NSPopUpButton(frame: .zero, pullsDown: false)
    model.translatesAutoresizingMaskIntoConstraints = false
    model.widthAnchor.constraint(equalToConstant: 200).isActive = true
    model.addItem(withTitle: "Model")
    let modelLabel = sheetLabel("Model")
    modelLabel.setContentHuggingPriority(.init(1), for: .horizontal)
    let modelRow = sheetStack([modelLabel, model], vertical: false)
    let generation = sheetLabel("Generation")
    let progress = NSProgressIndicator()
    progress.translatesAutoresizingMaskIntoConstraints = false
    progress.style = .bar
    progress.isIndeterminate = true
    let provenance = sheetLabel("", lines: 2)
    let draft = sheetEditor(170)
    let content = sheetStack([heading, notice, sheetLabel("Main question"), sheetEditor(66),
        sheetLabel("Additional context"), context, generation, status, account, modelRow,
        progress, provenance, draft], vertical: true)
    content.alignment = .width
    let document = SheetDocument(frame: NSRect(x: 0, y: 0, width: width, height: 500))
    document.autoresizingMask = .width
    document.addSubview(content)
    let body = NSScrollView()
    body.translatesAutoresizingMaskIntoConstraints = false
    body.hasVerticalScroller = true
    body.hasHorizontalScroller = false
    body.autohidesScrollers = true
    body.drawsBackground = false
    body.documentView = document

    let use = button("use")
    let actions = sheetStack([button("savePackage"), button("cancel"), button("generate"), use], vertical: false)
    let actionContainer = NSView()
    actionContainer.translatesAutoresizingMaskIntoConstraints = false
    actionContainer.addSubview(actions)
    let separator = NSBox()
    separator.translatesAutoresizingMaskIntoConstraints = false
    separator.boxType = .separator
    separator.heightAnchor.constraint(equalToConstant: 1).isActive = true
    let footer = sheetStack([separator, actionContainer], vertical: true)
    footer.spacing = 0
    root.addSubview(body)
    root.addSubview(footer)
    NSLayoutConstraint.activate([
        body.leadingAnchor.constraint(equalTo: root.leadingAnchor),
        body.trailingAnchor.constraint(equalTo: root.trailingAnchor),
        body.topAnchor.constraint(equalTo: root.topAnchor),
        body.bottomAnchor.constraint(equalTo: footer.topAnchor),
        footer.leadingAnchor.constraint(equalTo: root.leadingAnchor),
        footer.trailingAnchor.constraint(equalTo: root.trailingAnchor),
        footer.bottomAnchor.constraint(equalTo: root.bottomAnchor),
        content.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 20),
        content.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -20),
        content.topAnchor.constraint(equalTo: document.topAnchor, constant: 20),
        actions.leadingAnchor.constraint(equalTo: actionContainer.leadingAnchor, constant: 20),
        actions.trailingAnchor.constraint(equalTo: actionContainer.trailingAnchor, constant: -20),
        actions.topAnchor.constraint(equalTo: actionContainer.topAnchor, constant: 12),
        actions.bottomAnchor.constraint(equalTo: actionContainer.bottomAnchor, constant: -12),
    ])
    progress.isHidden = true
    provenance.isHidden = true
    draft.isHidden = true
    use.isHidden = true
    let controller = NSViewController()
    controller.view = root
    controller.preferredContentSize = NSSize(width: width, height: height)
    let window = NSWindow(contentViewController: controller)
    window.setContentSize(NSSize(width: width, height: height))
    func layoutAndCheck(_ stage: String) {
        for _ in 0..<4 {
            root.layoutSubtreeIfNeeded()
            body.layoutSubtreeIfNeeded()
            let viewport = body.contentView.bounds.size
            document.setFrameSize(NSSize(width: viewport.width,
                height: max(viewport.height, document.frame.height)))
            document.layoutSubtreeIfNeeded()
            document.setFrameSize(NSSize(width: viewport.width,
                height: max(viewport.height, ceil(content.fittingSize.height) + 40)))
            document.layoutSubtreeIfNeeded()
        }
        expect(abs(window.contentView!.frame.width - width) < 0.5, "\(stage): sheet width expanded")
        expect(abs(window.contentView!.frame.height - height) < 0.5, "\(stage): sheet height expanded")
        expect(abs(document.frame.width - body.contentView.bounds.width) < 0.5, "\(stage): horizontal document overflow")
        expect(abs(content.frame.minX - 20) < 0.5 && abs(document.frame.width - content.frame.maxX - 20) < 0.5,
               "\(stage): body horizontal margins are not 20 points")
        expect(abs(content.frame.minY - 20) < 0.5, "\(stage): body top margin is not 20 points")
        expect(document.frame.height - content.frame.maxY >= 19.5, "\(stage): body bottom margin is missing")
        expect(abs(actions.frame.minX - 20) < 0.5 && abs(actionContainer.frame.width - actions.frame.maxX - 20) < 0.5,
               "\(stage): footer horizontal margins are not 20 points")
        expect(abs(actions.frame.minY - 12) < 0.5 && abs(actionContainer.frame.height - actions.frame.maxY - 12) < 0.5,
               "\(stage): footer vertical margins are not 12 points")
        expect(abs(footer.frame.minY) < 0.5 && abs(body.frame.minY - footer.frame.maxY) < 0.5,
               "\(stage): footer does not remain below the scrollable body")
    }
    layoutAndCheck("initial \(width)")
    status.stringValue = String(repeating: "Long service status and error details. ", count: 80)
    account.stringValue = String(repeating: "Long research-group account name and usage information. ", count: 50)
    model.addItem(withTitle: String(repeating: "long-model-name-", count: 80))
    model.selectItem(at: 1)
    (context.documentView as! NSTextView).string = String(repeating: "UNBROKEN_CONTEXT_", count: 1000)
    progress.isHidden = false
    layoutAndCheck("long inputs and progress \(width)")
    provenance.stringValue = String(repeating: "Generated with selected model and scientific guidance. ", count: 50)
    provenance.isHidden = false
    draft.isHidden = false
    use.isHidden = false
    (draft.documentView as! NSTextView).string = String(repeating: "A synthetic interpretation paragraph for layout verification.\n\n", count: 30)
    layoutAndCheck("draft visible \(width)")
    let footerBefore = footer.frame
    document.scrollToVisible(draft.convert(draft.bounds, to: document).insetBy(dx: 0, dy: -20))
    layoutAndCheck("scrolled to draft \(width)")
    expect(footer.frame == footerBefore, "scrolling moved the fixed footer")
    expect(body.contentView.bounds.minY > 0, "the draft cannot be reached by vertical scrolling")
    let draftFrame = draft.convert(draft.bounds, to: document)
    expect(body.contentView.bounds.maxY - draftFrame.maxY >= 19.5,
           "automatically revealing the draft hides its bottom margin against the footer")
    if width == 620, let destination = ProcessInfo.processInfo.environment["FTITC_LAYOUT_SNAPSHOT_DIR"] {
        let url = URL(fileURLWithPath: destination).appendingPathComponent("interpretation-sheet-draft.png")
        if let bitmap = root.bitmapImageRepForCachingDisplay(in: root.bounds) {
            root.cacheDisplay(in: root.bounds, to: bitmap)
            try! bitmap.representation(using: .png, properties: [:])!.write(to: url)
        }
    }
    window.close()
}
checkSheet(width: 620)
checkSheet(width: 480)
if !failures.isEmpty {
    for failure in failures { print("FAIL: \(failure)") }
    exit(1)
}
print("PASS: Native interpretation actions and generation sheet retain bounded widths, body/footer margins and scrolling through long inputs, progress and draft display.")
