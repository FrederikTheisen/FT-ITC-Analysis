# Tandem regression fixtures

These native FTXTC projects were converted from two historical FTITC projects for the
`280-430-D2mut-1p6mM-JNK-200uM-1` three-run tandem experiment. They can be opened directly in
FT-ITC Analysis and are passed directly to `FTXTCReader` by the tests.

- `280-430-D2mut-1p6mM-JNK-200uM-1.ftxtc` contains the original project with saved
  no-back-mixing and fixed-back-mixing merges. SHA-256:
  `700702425b6d1352a6029d2060b997e87a2fc6481dbc402fa94fe9b817b17c9b`.
- `280-430-D2mut-1p6mM-JNK-200uM-1_process2.ftxtc` contains the reprocessed project with a
  systematic series of mixing fractions. SHA-256:
  `d0499efff129dbaf5dbe119555a37d7d6d72b09d14ede925223abe96a4105888`.

Each project contains three 26-injection source experiments and ten saved 78-injection tandem
merges. The saved merges are historical reference outputs for concentration and heat regression
tests; they are not regenerated when the tests run.
