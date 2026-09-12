# FTXTC JSON Schemas

The root-level `manifest.schema.json`, `project.schema.json`, and
`component.schema.json` files are the current schema entry points. They describe
the JSON entries emitted by the current FTXTC writer: package schema 1.6 and
project schema 4.

The numbered directories contain retained historical schemas. They document the
wire shapes for the package versions that the reader continues to migrate; they
are not writer targets. The application writes only `.ftxtc` package schema 1.6
and never writes the retired `.ftitc` format.

JSON Schema validates one JSON document at a time. The FTXTC reader additionally
validates ZIP entry safety and uniqueness, manifest membership, checksums,
cross-document IDs and references, and FTXB header and matrix dimensions. Those
package-level checks are deliberately outside JSON Schema and are tested through
the reader.
