# Tandem regression fixtures

These gzip files are lossless compressed copies of two historical FTITC projects for the
`280-430-D2mut-1p6mM-JNK-200uM-1` three-run tandem experiment. They are compressed only to
keep the repository and test output smaller; tests decompress them before passing the original
FTITC stream to `FTITCReader`.

- `tandem-original.ftitc.gz` contains the original project with saved no-back-mixing and
  fixed-back-mixing merges. Uncompressed SHA-256:
  `8979e5ac67a541c5355444f21204f87e10f914459a7a7fe0f8b1f5014fdda2ad`.
- `tandem-process2.ftitc.gz` contains the reprocessed project with a systematic series of
  mixing fractions. Uncompressed SHA-256:
  `82d1cd6c3d9d0d92f1b7a22cacdae5ea69cc8db4ba5153f21d6804269826e239`.

Each project contains three 26-injection source experiments and ten saved 78-injection tandem
merges. The saved merges are historical reference outputs for concentration and heat regression
tests; they are not regenerated when the tests run.
