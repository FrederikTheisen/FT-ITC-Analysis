#!/usr/bin/env ruby
# Convert the published SEDPHAT ITCdhTable.DAT integrated normalized heats
# into the legacy FT-ITC .DH representation.  No thermogram processing or
# refitting is performed: NDH is copied as the source heat, with only the
# documented cal/mol -> microcal injection-energy conversion applied.

source_path, output_path = ARGV
abort 'usage: create-sedphat-twosite-dh.rb source.DAT output.DH' unless source_path && output_path

rows = File.readlines(source_path, chomp: true).drop(1).each_with_object([]) do |line, parsed|
  next if line.strip.empty?
  fields = line.split(/\s+/)
  next if fields.length < 6
  next if fields[0] == '--'

  dh = Float(fields[0])
  volume_ul = Float(fields[1])
  ndh = fields[5] == '--' ? nil : Float(fields[5])
  parsed << { dh: dh, volume_ul: volume_ul, ndh: ndh }
end

abort 'expected 21 source injections' unless rows.length == 21

# Source page metadata: 4.5 uM cell, 50 uM syringe, 1414.1 uL cell.
# NDH is cal/mol; q[uCal] = NDH * V[uL] * Csyr[M] * 1e-6 L/uL * 1e6 uCal/cal.
syringe_m = 50e-6
heat_microcal = rows.map do |row|
  if row[:ndh]
    row[:ndh] * row[:volume_ul] * syringe_m
  else
    # The source does not provide NDH for the first (excluded) injection;
    # retain its direct DH value as supplied.  It is excluded by the reader.
    row[:dh]
  end
end

lines = [
  rows.length.to_s,
  "0,#{rows.length},0,0,0",
  '25.0,0.0045,0.05,1.4141,0',
  '0',
  '0'
]
rows.zip(heat_microcal).each { |row, heat| lines << "#{row[:volume_ul].to_s},#{heat.to_s}" }

File.write(output_path, lines.join("\n") + "\n")
