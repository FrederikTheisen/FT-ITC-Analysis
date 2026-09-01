# MicroCal `.itc` raw-data format

This document describes the plain-text `.itc` format used by MicroCal VP-ITC,
ITC200, and MicroCal PEAQ-ITC acquisition software. It is a transcription and
clarification of a one-page format note titled *ITC parameters* (Leigh Raymond,
10 January 2008), cross-checked against files from all three instrument
families and the FT-ITC Analysis importer.

This is not a vendor specification. The original note gives names for many
fields but does not define every unit or enumerate every software-version
variant. Fields marked **uncertain** below retain that ambiguity.

## Observed variants

All examined files use the same section order and the same first three sample
columns. The principal differences are:

| Instrument/software identifier | Sample columns | Injection-marker fields | Header differences |
| --- | ---: | ---: | --- |
| `VPITC...` / `VPViewer2000` | 3 or 7 | 3 | 7 `#` records; 16 `%` records |
| `ITC200_...` / `ITC200` | 9 | 4 | 7 `#` records; 17 `%` records |
| `MICROCALITC_MAL...` / `MicroCalITC` | 9 | 4 | 7 or 8 `#` records; 17 `%` records |

The counts describe the supplied reference files and source note, not rigid
version requirements. Readers should use the record prefixes and tolerate the
optional fields described below.

## Overview

The file is line-oriented and contains four sections in this order:

1. an experiment header and injection program, using `$` records;
2. sample and cell properties, using `#` records;
3. comments and instrument metadata, using `?` and `%` records; and
4. the measured data stream, introduced by `@0`, with later `@` records marking
   injections.

Blank lines are not significant. Numeric lists are comma-separated. Examples
in this document omit incidental whitespace.

## Experiment header and injection program (`$`)

The beginning of the file is positional:

```text
$ITC
$65
$NOT
$30
$60
$307
$5
$2
$ADCGainCode: 3
$False,True,True
$10,20,120,2
...
```

| Line | Example | Meaning | Unit or values |
| ---: | --- | --- | --- |
| 1 | `$ITC` | File signature | Literal text |
| 2 | `$65` | Number of injections | Count |
| 3 | `$NOT` | Edit mode | **Uncertain**; `$NOT` in the source note |
| 4 | `$30` | Cell temperature | degrees Celsius |
| 5 | `$60` | Initial delay | seconds |
| 6 | `$307` | Stirring speed | revolutions per minute |
| 7 | `$5` | Reference power | microcalories per second |
| 8 | `$2` | Feedback mode | Instrument-specific numeric code |
| 9 | `$ADCGainCode: 3` | ADC gain code | The source note says code `3` applies 1.25 V |
| 10 | `$False,True,True` | Check temperature; fast equilibration; automatic operation | Booleans, in that order |
| 11 onward | `$10,20,120,2` | Injection-program row | See below |

Each injection-program row has four fields:

| Position | Field | Unit |
| ---: | --- | --- |
| 1 | Injection volume | microlitres |
| 2 | Injection duration | seconds |
| 3 | Spacing between injections | seconds |
| 4 | Filter period | seconds |

The number of injection-program rows normally equals the injection count on
line 2. These rows describe the planned protocol, not necessarily the number
of injections that were completed. An interrupted run can contain fewer `@`
injection markers in the data stream. Different injections may also have
different settings.

## Sample and cell properties (`#`)

The injection program is followed by seven positional `#` records and, in
some newer files, an eighth record:

```text
#0
#0
#0
#1.4791
#30
#29.5188
#16.47
[#1]
```

| Position | Meaning | Unit |
| ---: | --- | --- |
| 1 | Not applicable/reserved | **Uncertain** |
| 2 | Syringe concentration | millimolar |
| 3 | Cell concentration | millimolar |
| 4 | Cell volume | millilitres |
| 5 | Run temperature | degrees Celsius |
| 6 | Slope for DP mid-gain feedback (high-gain mode) | Instrument calibration value |
| 7 | Half-time | seconds |
| 8 | Optional instrument/software flag | **Uncertain**; observed as `1` in some `MicroCalITC` files |

## Comments and instrument metadata (`?` and `%`)

A line beginning with `?` contains the operator's free-text comments:

```text
?Sample A into Sample B
```

It is followed by positional instrument metadata records beginning with `%`.
The first eleven records have the same layout in the examined variants:

| Position | Meaning |
| ---: | --- |
| 1 | Cell serial number, for example `VPITC0707.897` |
| 2 | Cell volume |
| 3 | Slope for DP mid-gain feedback (high-gain mode) |
| 4 | Jacket-temperature read offset, jacket-temperature read slope |
| 5 | Shield-temperature read offset, shield-temperature read slope |
| 6 | Delta-temperature read value |
| 7 | Reference-cell calibration-heater value, sample-cell calibration-heater value |
| 8 | Minimum safe temperature, maximum safe temperature |
| 9 | T2 (shield) set-point offset, T2 set-point slope |
| 10 | ATP-read offset, ATP-read slope |
| 11 | ATP-output slope, ATP-output offset |

The remaining records differ by instrument generation:

| VP-ITC position | ITC200/PEAQ-ITC position | Meaning |
| ---: | ---: | --- |
| - | 12 | Additional instrument flag; **uncertain**, observed as `0` |
| 12 | 13 | Steps per inch, maximum number of steps |
| 13 | 14 | Syringe constant in microlitres per inch |
| 14 | 15 | Stirring speeds for RPM settings 1, 2, 3, and 4 |
| 15 | 16 | DP correction constants `A0` through `A4` |
| 16 | 17 | Acquisition-software name, version, and sometimes run time |

Most of these values are instrument calibration or hardware-control metadata.
Their calibration units are not stated in the source note.

The first `%` record identifies the instrument family. Observed prefixes are
`VPITC` for VP-ITC, `ITC200_` for MicroCal ITC200, and `MICROCALITC_MAL` for
MicroCal PEAQ-ITC. The final record contains strings such as
`VPViewer2000 Ver: 1.4.21`, `ITC200 Ver: 1.26.3`, or
`MicroCalITC Ver: 1.30.4 Run time:<date and time>`.

## Measured data stream (`@` and numeric rows)

The literal marker `@0` ends the header and begins the data stream:

```text
@0
2.00,4.644075,30.00613,-0.000190,29.020255,-0.0076,1.395
...
@1,10.0,20.0[,122.1]
...
```

### Sample rows

Every examined variant begins a numeric sample row with the same three fields:

| Position | Label | Meaning | Typical unit |
| ---: | --- | --- | --- |
| 1 | `Time` | Elapsed time | seconds |
| 2 | `DP` | Differential power | microcalories per second |
| 3 | `Temp` | Cell temperature | degrees Celsius |

Additional diagnostic columns depend on the acquisition software. The scanned
format note assigns the following labels to columns 4-7:

| Position | Label | Meaning | Typical unit |
| ---: | --- | --- | --- |
| 4 | `DT` | Differential temperature | degrees Celsius |
| 5 | `Shield T` | Shield temperature | degrees Celsius |
| 6 | `ATP` | **Uncertain** instrument-control/readback channel | Not documented |
| 7 | `JFB1` | **Uncertain** feedback/readback channel | Not documented |

The supplied VPViewer 1.4.21 files omit those diagnostic fields and contain
only columns 1-3. The supplied ITC200 and MicroCalITC files contain nine
columns; the meanings of columns 8 and 9 are not identified by the source
note. Consumers should require at least three values and tolerate additional
values.

### Injection-marker rows

An injection is marked in the data stream by an `@` record:

```text
@<number>,<volume>,<duration>[,<time>]
```

| Position | Meaning | Unit |
| ---: | --- | --- |
| 1 | One-based injection number | Integer |
| 2 | Delivered volume | microlitres |
| 3 | Injection duration | seconds |
| 4 | Optional injection time | seconds |

VPViewer reference files contain the first three fields. ITC200 and
MicroCalITC reference files also contain the injection time. When the time is
omitted, it is the time of the immediately preceding sample row. The original
photographed note only shows `@0` and sample data; the injection-marker layout
is confirmed by representative files and the FT-ITC Analysis importer.

## FT-ITC Analysis import behavior

The current importer treats the header as positional and uses the following
subset of the format:

- header lines 4-8: target temperature, initial delay, stirring speed,
  reference power, and feedback mode;
- injection-program rows: volume, duration, spacing/delay, and filter period;
- `#` records 2-4: syringe concentration, cell concentration, and cell volume;
- the `?` record: comments;
- the first `%` record: MicroCal instrument identification;
- `@0`: start of the data stream;
- later `@` records: injection number, volume, duration, and optional injection
  time;
- numeric sample columns 1-3: time, differential power, and temperature.

Concentrations are converted from millimolar to molar, cell volume from
millilitres to litres, injection volume from microlitres to litres, and
differential power from microcalories per second to watts. Extra sample columns
and unused calibration records are currently ignored. If an injection marker
does not contain a time, or its stated time differs from the preceding sample
by 10 seconds or more, the importer uses the preceding sample time.

The importer initially creates injections from the planned protocol. It then
matches the actual data-stream markers by their one-based number. Consequently,
a file can validly describe more planned injections than were recorded; the
application's import validation handles the unrecorded protocol entries. A
marker without a matching protocol row is instead created from the marker and,
when possible, the corresponding protocol-row template.

For concatenated runs, FT-ITC Analysis recognizes a new segment when the
optional injection time moves backwards by more than one second while the
injection numbering continues. Three-field VP-ITC markers do not carry enough
information for that time-reset detection.

Because the parser relies on record order in parts of the header, a writer
should preserve the documented order even when a value is unknown. It should
emit a placeholder value instead of deleting a positional line.

## Portable format skeleton

```text
$ITC
$<injection-count>
$NOT
$<cell-temperature>
$<initial-delay>
$<stirring-speed>
$<reference-power>
$<feedback-mode>
$ADCGainCode: <code>
$<check-temperature>,<fast-equilibration>,<automatic>
$<volume>,<duration>,<spacing>,<filter-period>
... one row per programmed injection ...
#<reserved>
#<syringe-concentration>
#<cell-concentration>
#<cell-volume>
#<run-temperature>
#<dp-mid-gain-feedback-slope>
#<half-time>
[#<optional-instrument-flag>]
?<comments>
%<cell-serial-number>
%<cell-volume>
%<dp-mid-gain-feedback-slope>
%<jacket-read-offset>,<jacket-read-slope>
%<shield-read-offset>,<shield-read-slope>
%<delta-temperature-read>
%<reference-cell-cal-heater>,<sample-cell-cal-heater>
%<minimum-safe-temperature>,<maximum-safe-temperature>
%<t2-set-point-offset>,<t2-set-point-slope>
%<atp-read-offset>,<atp-read-slope>
%<atp-output-slope>,<atp-output-offset>
[%<optional-itc200-instrument-flag>]
%<steps-per-inch>,<maximum-number-of-steps>
%<syringe-constant>
%<rpm-1>,<rpm-2>,<rpm-3>,<rpm-4>
%A0=<a0>,A1=<a1>,A2=<a2>,A3=<a3>,A4=<a4>
%<software-name-and-version>
@0
<time>,<dp>,<temp>[,<dt>,<shield-temp>,<atp>,<jfb1>[,<field-8>,<field-9>]]
...
@<injection-number>,<volume>,<duration>[,<time>]
...
```

## Provenance and limitations

- Primary source: photographed/scanned page *ITC parameters*, attributed in
  the PDF metadata to Leigh Raymond and dated 10 January 2008.
- Cross-check: the supplied VP-ITC, ITC200, and MicroCal PEAQ-ITC reference
  files, plus `MicroCalITC200Reader`.
- The labels `ATP` and `JFB1`, sample columns 8-9, optional instrument flags,
  several calibration units, and the semantic meaning of `$NOT` are not
  explained by the source and therefore remain intentionally undocumented
  rather than guessed.
