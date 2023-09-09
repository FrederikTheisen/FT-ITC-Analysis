//
//  AutoExpandingTextField.swift
//  AnalysisITC
//
//  Created by Frederik Theisen on 09/09/2023.
//

import Cocoa

class AutoExpandingTextField: NSTextField {

    override var intrinsicContentSize: NSSize {
        // Guard the cell exists and wraps
        guard let cell = self.cell, cell.wraps else {return super.intrinsicContentSize}

        // Use intrinsic width to jive with autolayout
        let width = super.intrinsicContentSize.width

        // Set the frame height to a reasonable number
        self.frame.size.height = 750.0

        // Calcuate height
        let height = cell.cellSize(forBounds: self.frame).height

        return NSMakeSize(width, height);
    }

    override func textDidChange(_ notification: Notification) {
        super.textDidChange(notification)
        super.invalidateIntrinsicContentSize()
    }
    
}
