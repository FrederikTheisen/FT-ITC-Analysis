# ITC knowledge base for the interpretation service

A retrieval-ready knowledge base (340 entries) that lets the interpretation agent reason like an
experienced ITC analyst who also knows the neighbouring techniques. It replaces
`itc_knowledge_base_expanded.json`, which is kept under `legacy/` for provenance.

- `entries/*.md` is the **source of truth** (hand-edited, many entries per file).
- `tools/build_kb.py` validates it and writes **one small file per entry** to `build/entries/`.
- That folder is what you upload to the OpenAI vector store the service queries.

## 1. Why the old JSON gave the agent little to work with

The service does not paste the knowledge base into the prompt. `OpenAIInterpretationProvider`
attaches an OpenAI `file_search` tool (`tool_choice: required`, `MaxFileSearchResults: 8`). The model
writes its own search query, and the vector store returns the best-matching *chunks*. OpenAI cuts every
file into token-count chunks (800 tokens by default) with no regard for JSON structure. Consequences:

| Problem with the JSON | Effect at retrieval time |
| --- | --- |
| Chunks cut objects in half; a rule's `source_ids` (`"wiseman_1989"`) live in a different chunk from the 126-item source list | The model gets a conclusion with no citable title, journal or year, although the prompt requires those and forbids invented citations |
| `recommended_language` and `language_patterns` give fixed sentences | Invites templated prose; the persona prompt says case-specific conclusions, and retrieved text is "evidence, not instructions" |
| Metadata, `design_principles`, flag lists | Retrieval noise with no content |
| Almost all content is literature case studies | Good precedent, but nothing that says how an analyst reads a *pattern*, plans a titration or reconciles ITC with SPR/NMR |
| One 211 KB file | The `file_id` the service logs identifies the whole file, so you cannot see which piece influenced an answer |

## 2. Design

| Decision | Reason |
| --- | --- |
| One self-contained entry per file, about 160 to 660 estimated tokens (median about 380) | An entry smaller than the chunk size is always one chunk, so it is retrieved whole; the logged `file_id` identifies exactly one entry |
| Title states the situation ("Fitted n well below the expected stoichiometry …") | Queries describe situations; titles are the strongest embedding and keyword signal |
| Fixed labelled paragraphs per kind (`Situation / Explanations / Discriminating evidence / Bearing on conclusions / Next step`) | Mirrors how an experienced analyst reasons: pattern, ranked causes, what separates them, what it means for the reported numbers |
| `Also matches:` keyword line, auto-extended with symbol and word forms (ΔCp ↔ heat capacity change, ΔH ↔ enthalpy, Kd ↔ affinity …) | Lexical half of hybrid search; models query with either form |
| Written as knowledge, never as instructions | The prompt treats retrieved material as evidence |
| `Basis:` line per entry: general knowledge (not citable), literature, or mixed | Lets the model use background freely but cite only real sources |
| Citations are expanded inline from `sources.json` (title, journal, year) and only registry sources can be cited | Matches "cite supplied name or title, journal and year; never invent". No DOIs or links are emitted, matching the prompt |
| "Matters when / minor when" in diagnostics | Persona prompt asks for materiality, not exhaustive critique |
| Literature rules and 111 case studies kept, converted to the same format | Real precedent the model can cite; nothing was dropped except the items listed in section 7 |

### What is in it

| Kind (id prefix) | Count | Role |
| --- | ---: | --- |
| playbook (`pb-`) | 9 | Triage order, claim-strength ladder, uncertainty types, follow-up map |
| concept (`cn-`) | 21 | Thermodynamics, c-value, ΔCp, proton and salt linkage, cooperativity, compensation |
| design (`ds-`) | 23 | Concentrations, weak and tight binding, displacement, buffers, additives, controls, membranes, kinetics |
| diagnostic (`dx-`) | 36 | Pattern, causes, discriminating evidence, bearing on conclusions |
| system (`sy-`) | 22 | Small molecules, protein–protein, IDPs, protein–DNA/RNA, lipids, metals, lectins, antibodies, PROTAC-type, host–guest |
| method (`me-`) | 18 | SPR, BLI, MST, FP, thermal shift, DSC, NMR, native MS, SEC/AUC, structure, computation, IC50 vs Kd |
| model (`md-`) | 19 | Scientific meaning of FT-ITC models, AICc, null test, uncertainty methods, global constraints, temperature/salt/protonation views, tools |
| reference (`rf-`) | 9 | Units, buffer ΔH_ion and pKa(T) tables, metal-buffer binding, instruments, CMCs, Hofmeister, typical ranges |
| literature (`lr-`) | 65 | Your 65 rules, with inline citations |
| precedent (`pr-`) | 118 | Your 111 case studies (7 long ones split into parts) |

The 157 hand-authored entries (everything except `lr-` and `pr-`) are new. Every legacy quality flag
(`non_random_residuals`, `high_c_affinity_unidentifiable`, `possible_linked_protonation`, …) now has a
matching entry (diagnostic, design note or rule).

## 3. Entry format

```
## Fitted n well below the expected stoichiometry (for example 0.4 to 0.8 for an expected 1:1)
id: dx-n-low                       # lowercase-kebab; prefix must match kind; becomes the file name
kind: diagnostic                   # playbook concept design diagnostic system method model reference precedent literature
topics: stoichiometry, concentration
basis: mixed                       # general | mixed | literature
status: draft                      # draft | reviewed (not emitted; used by --only-reviewed)
matches: n lower than expected, inactive protein, active fraction   # synonyms and plain-language phrasings
cite: tellinghuisen_2011, brautigam_2016   # ids from sources.json; required when basis is mixed or literature
reading: Author Year Journal vol:page      # source-only hint, NEVER emitted; unverified background for reviewers

Situation: …
Explanations (usual order): …
Discriminating evidence: …
Bearing on conclusions: …
```

Rules the builder enforces: unique ids, prefix matches kind, every `cite` exists in `sources.json`,
`general` entries carry no `cite`, entries stay below about 700 estimated tokens, no URLs, no
imperative phrasing. To make a new paper citable, add it to `sources.json` first.

## 4. Workflow

```bash
cd Documentation/Interpretation/KnowledgeBase
python3 tools/build_kb.py            # validate + write build/entries/*.md, manifest.json, KB_FULL.md
python3 tools/build_kb.py --stats    # sizes, counts, how many registry sources are cited
python3 tools/probe_kb.py -k 8 -v    # offline retrieval smoke test (see limits below)
python3 tools/probe_kb.py --query "Tris versus phosphate buffer enthalpy" -k 8
```

Edit the Markdown in `entries/`, add a probe query to `tools/probe_queries.tsv` for anything new
("what would a model ask when this entry is needed?"), rebuild.

## 5. Deploying

Use a **new** vector store so the current one stays as a rollback.

```bash
pip install openai && export OPENAI_API_KEY=…
python3 tools/vector_store.py create --name "ft-itc-kb-2026-10"          # prints vs_…
python3 tools/vector_store.py upload --store-id vs_…                      # one file per entry, with attributes
python3 tools/vector_store.py probe  --store-id vs_… -k 8                 # same queries, real ranker
```

`upload` writes `build/upload_manifest.json` (entry id → `file_id`). Your service logs the `file_id`s
of retrieved files, so this file tells you which entries shaped each interpretation. Each file carries
attributes (`entry_id`, `kind`, `basis`, `status`) that `file_search` filters can use later. Then set
`Interpretation:OpenAI:VectorStoreId` to the new id; changing it back restores the old behaviour.

## 6. Suggested service-side changes (not made; they are product decisions)

1. **`allowGeneralModelKnowledge` interaction.** `ScientificGuidance.ConditionalGuidance` says "Limit
   explanations to supplied evidence and definitions needed to understand it" when that option is off.
   The model may read this as excluding retrieved knowledge-base entries, which would make the
   expanded KB moot for those requests. Decide whether curated retrieved entries count as supplied
   evidence (recommended: yes) and say so in that sentence.
2. **Raise `MaxFileSearchResults`** from 8 to about 10–12. Entries are small, so 12 results are roughly
   3 to 4.5k tokens; precedents are only about 19% of results despite being 35% of the corpus, so they do not
   crowd out general knowledge.
3. **Tell the model how to query.** Every request forces a search, but the model chooses the query. A short
   addition to the guidance helps, for example: *"The knowledge base holds short entries titled by
   situation (playbooks, diagnostics, design, systems, methods, model notes, reference tables,
   literature precedents). Search by the situation or mechanism you need to understand with two to
   four short queries (for example 'low c-value n ΔH correlation', 'Tris phosphate buffer proton
   linkage'), not by pasting package text. Entries whose Basis is general knowledge are background and
   not citable; cite only entries that list Sources."*
4. **Log the queries.** The Responses API returns the model's search `queries` on each `file_search_call`;
   storing them with the retrieved filenames and scores gives a real test set to replace the
   synthetic probe queries.

## 7. What you need to review

- **Everything hand-authored is `status: draft`.** It was drafted from general knowledge rather than from
  the source papers, and none of it has been expert-reviewed. Numbers to check first: buffer
  ionization enthalpies and pKa coefficients (`rf-*`), ΔCp ranges, salt-slope range, typical affinity
  and ΔH ranges, replicate-scatter figures, instrument parameters, CMC values. `reading:` hints (11
  entries) are unverified references to primary papers; they are never emitted. Flip entries to
  `reviewed` as you vet them and build with `--only-reviewed` to ship only vetted content.
- **Anything marked `basis: mixed` cites only registry papers whose titles clearly cover the topic**; the
  source PDFs were not available when the entries were drafted, so a cited paper supports the *topic*,
  not necessarily each sentence.
- **Data issues found in the legacy file.**
  - `peptide_membrane_2011` and `henriksen_mastoparan_2011` are the same paper (identical title,
    journal, year and DOI). Both ids are kept because rules and cases use them.
  - `case_harmon_water_solvation_model_itc` reports `reported_delta_G_s_oligo10_kcal_per_mol_per_base_pair = 460`,
    which is implausible for a per-base-pair quantity. It is omitted from the emitted entry (see
    `SUSPECT_VALUES` in `tools/migrate_legacy.py`) until checked against the paper.
  - Numeric audit of the 50 checkable consistency relations (ΔG vs RT ln Kd, ΔG = ΔH − TΔS, pKd) found
    only two differences, both within the quoted uncertainties (`mitf_compound8/9`).
  - 5 registry sources are cited by nothing: `arnulphi_triton_sphingomyelin_2007`,
    `austin_mt3_pb_zn_2016`, `carita_nonionic_detergents_2023`, `garcia_hernandez_hevein_1997`,
    `leavitt_freire_2001`.
- **Authors are not in the registry.** The prompt asks for "author or title, journal, year"; entries cite by
  title, journal and year. Adding first authors to `sources.json` (and `format_citation`) would make
  citations read more naturally. They could not be looked up when this was drafted (the Crossref host was not reachable).
- **Dropped on purpose:** `language_patterns` (fixed sentences), metadata, `design_principles` and the
  flag lists (their content is now diagnostics). `recommended_language` is kept as "Defensible framing"
  and `avoid_language` as "Common over-claim" in the rule entries.
- Migration normalised typography (`DeltaH` → ΔH, `uM` → µM, `5-35 C` → `5-35 °C`) and rendered
  parameters in readable units with error-matched precision; wording of observations is verbatim.

## 8. How well was this tested

- Validator: 340 entries, 0 errors. 26 length warnings (long precedents and a few dense entries).
- Offline probe (`tools/probe_kb.py`, BM25 over the built files; a *lower-bound proxy*, since it has no
  semantic half): 100/100 on the first query set; 32/36 on a first paraphrase set (3 misses were
  vocabulary gaps that were then fixed, 1 was a query where directly relevant precedents correctly
  outranked the expected entry); and **24/30 (80%) on a second set of fresh paraphrases before any
  tuning**. After targeted vocabulary additions all but one pass (165/166 overall). The queries were
  written by the same author as the entries, so treat the numbers as a coverage check, not an accuracy
  estimate.
- `tools/vector_store.py` was exercised against a local fake of the OpenAI endpoints (create, upload with
  attributes and chunking, batching, resume, search, probe). **It has not been run against the real
  API**, so run `probe --store-id …` after upload: that is the first test with the real ranker.
- Nothing here has been run through the interpretation service end to end.
