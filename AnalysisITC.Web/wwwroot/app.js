const viewerBuild = "2026.08.25-correlation.1";
document.documentElement.dataset.viewerBuild = viewerBuild;

const state = {
  token: null,
  document: null,
  workspace: "experiments",
  experimentIndex: 0,
  view: "metadata",
  preferredExperimentView: "metadata",
  processingMode: "raw",
  showIntegrationRanges: true,
  fitIndex: 0,
  resultIndex: 0,
  correlationViewKeysByResult: {},
  resultMemberIndex: 0,
  resultEvaluationTemperature: null,
  advancedAnalysisKind: null,
  advancedPlotKey: null
};
const $ = (id) => document.getElementById(id);
const colors = { raw: "#718086", teal: "#087e78", coral: "#ca644f", amber: "#c18b29", blue: "#386c93", purple: "#76558e", pale: "rgba(8,126,120,.16)" };
const numberFormatter = new Intl.NumberFormat("en-US", { useGrouping: false, maximumSignificantDigits: 6 });
const plotConfig = { responsive: true, displaylogo: false, scrollZoom: true, modeBarButtonsToRemove: ["lasso2d", "select2d"] };

document.addEventListener("DOMContentLoaded", async () => {
  bindEvents();
  await refreshToken();
});

function bindEvents() {
  $("file-input").addEventListener("change", (event) => {
    $("file-label").textContent = event.target.files[0]?.name || "Select an .ftxtc, .ftitc, .itc, .nitc, or .opj file";
  });
  $("upload-form").addEventListener("submit", openFile);
  $("new-file-button").addEventListener("click", resetViewer);
  $("experiments-workspace-button").addEventListener("click", () => setWorkspace("experiments"));
  $("results-workspace-button").addEventListener("click", () => setWorkspace("results"));
  $("fit-select").addEventListener("change", (event) => {
    state.fitIndex = Number(event.target.value);
    renderView();
  });
  $("result-member-select").addEventListener("change", (event) => {
    state.resultMemberIndex = Number(event.target.value);
    renderResultMember();
  });
  $("result-evaluation-temperature").addEventListener("input", (event) => {
    state.resultEvaluationTemperature = Number(event.target.value);
    renderTemperatureParameterEvaluation(currentResult());
  });
  $("advanced-plot-select").addEventListener("change", (event) => {
    state.advancedPlotKey = event.target.value;
    renderAdvancedAnalysis(currentResult());
  });
  $("result-correlation-view-select").addEventListener("change", (event) => {
    const result = currentResult();
    if (result) state.correlationViewKeysByResult[result.key] = event.target.value || null;
    renderResultCorrelation(result);
  });
  $("processed-mode-raw").addEventListener("click", () => setProcessingMode("raw"));
  $("processed-mode-corrected").addEventListener("click", () => setProcessingMode("corrected"));
  $("processed-integration-ranges").addEventListener("click", toggleIntegrationRanges);
}

async function refreshToken() {
  const response = await fetch("/api/viewer/token", { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) throw new Error("Could not initialize secure uploads.");
  state.token = (await response.json()).requestToken;
}

async function openFile(event) {
  event.preventDefault();
  const file = $("file-input").files[0];
  if (!file) return showError("Choose a .ftxtc, .ftitc, .itc, .nitc, or .opj file.");
  if (file.size > 50 * 1024 * 1024) return showError("The selected file is larger than 50 MB.");

  const button = $("open-button");
  button.disabled = true;
  button.textContent = "Opening…";
  showError(null);
  try {
    if (!state.token) await refreshToken();
    const form = new FormData();
    form.append("file", file);
    const response = await fetch("/api/viewer/open", {
      method: "POST",
      credentials: "same-origin",
      headers: { "X-CSRF-TOKEN": state.token },
      body: form
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => null);
      throw new Error(problem?.detail || "The file could not be opened.");
    }
    state.document = await response.json();
    state.workspace = "experiments";
    state.experimentIndex = 0;
    state.fitIndex = 0;
    state.view = "metadata";
    state.preferredExperimentView = "metadata";
    state.processingMode = "raw";
    state.showIntegrationRanges = true;
    state.resultIndex = 0;
    state.resultMemberIndex = 0;
    state.correlationViewKeysByResult = {};
    state.resultEvaluationTemperature = null;
    state.advancedAnalysisKind = null;
    state.advancedPlotKey = null;
    renderDocument();
  } catch (error) {
    showError(error.message);
    await refreshToken().catch(() => {});
  } finally {
    button.disabled = false;
    button.textContent = "Open file";
  }
}

function showError(message) {
  const box = $("upload-error");
  box.hidden = !message;
  box.textContent = message || "";
}

function resetViewer() {
  state.document = null;
  state.workspace = "experiments";
  state.correlationViewKeysByResult = {};
  $("viewer").hidden = true;
  $("upload-panel").hidden = false;
  $("upload-form").reset();
  $("file-label").textContent = "Choose a .ftxtc, .ftitc, .itc, .nitc, or .opj file";
  ["plot", "result-comparison-plot", "result-correlation-plot", "result-fit-plot", "advanced-analysis-plot"].forEach((id) => window.Plotly?.purge?.($(id)));
}

function renderDocument() {
  const doc = state.document;
  $("upload-panel").hidden = true;
  $("viewer").hidden = false;
  $("document-name").textContent = doc.displayName;
  $("document-format").textContent = `${doc.format.toUpperCase()}${doc.formatVersion ? ` · version ${doc.formatVersion}` : ""}`;
  $("document-summary").textContent = `${doc.experiments.length} experiment${doc.experiments.length === 1 ? "" : "s"} · ${formatBytes(doc.sizeBytes)} · ${doc.analysisResults.length} saved analysis result${doc.analysisResults.length === 1 ? "" : "s"}`;
  const warning = $("document-warnings");
  warning.hidden = !doc.warnings?.length;
  warning.textContent = doc.warnings?.join(" ") || "";

  const resultsButton = $("results-workspace-button");
  resultsButton.hidden = !doc.analysisResults.length;
  renderResultList();
  renderExperiment();
  setWorkspace("experiments");
}

function setWorkspace(workspace) {
  if (workspace === "results" && !state.document?.analysisResults?.length) workspace = "experiments";
  state.workspace = workspace;
  const experimentsActive = workspace === "experiments";
  $("experiments-workspace-button").setAttribute("aria-selected", String(experimentsActive));
  $("results-workspace-button").setAttribute("aria-selected", String(!experimentsActive));
  $("experiments-workspace").hidden = !experimentsActive;
  $("results-workspace").hidden = experimentsActive;
  if (experimentsActive) {
    window.Plotly?.purge?.($("result-comparison-plot"));
    window.Plotly?.purge?.($("result-correlation-plot"));
    window.Plotly?.purge?.($("result-fit-plot"));
    window.Plotly?.purge?.($("advanced-analysis-plot"));
    renderView();
  } else {
    window.Plotly?.purge?.($("plot"));
    renderResult();
  }
}

function renderExperimentList() {
  const list = $("experiment-list");
  if (!state.document) return list.replaceChildren();
  list.replaceChildren(...state.document.experiments.map((experiment, index) => {
    const temperature = formatNumber(experiment.measuredTemperatureCelsius, " °C");
    const injections = `${experiment.injectionCount} injection${experiment.injectionCount === 1 ? "" : "s"}`;
    return selectionListItem(experiment.name, `${temperature} · ${injections}`, index === state.experimentIndex, () => {
      if (index === state.experimentIndex) return;
      state.experimentIndex = index;
      state.fitIndex = 0;
      const nextExperiment = state.document.experiments[index];
      state.view = nextExperiment.availableViews.includes(state.preferredExperimentView)
        ? state.preferredExperimentView
        : "metadata";
      state.processingMode = "raw";
      renderExperiment();
    });
  }));
}

function renderResultList() {
  const list = $("result-list");
  if (!state.document?.analysisResults?.length) return list.replaceChildren();
  list.replaceChildren(...state.document.analysisResults.map((result, index) => {
    const details = [result.modelName, result.isGlobal ? "Global" : "Individual", result.date ? formatDate(result.date) : null]
      .filter(Boolean)
      .join(" · ");
    return selectionListItem(result.name || `Result ${index + 1}`, details, index === state.resultIndex, () => {
      if (index === state.resultIndex) return;
      state.resultIndex = index;
      state.resultMemberIndex = 0;
      state.resultEvaluationTemperature = null;
      state.advancedAnalysisKind = null;
      state.advancedPlotKey = null;
      renderResult();
    });
  }));
}

function selectionListItem(primaryText, secondaryText, selected, activate) {
  const item = document.createElement("li");
  const button = document.createElement("button");
  button.type = "button";
  button.className = "selection-item";
  button.setAttribute("aria-current", String(selected));
  const primary = document.createElement("span");
  primary.className = "selection-primary";
  primary.textContent = primaryText;
  const secondary = document.createElement("span");
  secondary.className = "selection-secondary";
  secondary.textContent = secondaryText;
  button.append(primary, secondary);
  button.addEventListener("click", activate);
  item.append(button);
  return item;
}

function renderExperiment() {
  const experiment = currentExperiment();
  if (!experiment) return;
  renderExperimentList();
  $("experiment-name").textContent = experiment.name;
  $("experiment-source").textContent = `${experiment.sourceFileName} · ${experiment.sourceFormat}`;
  const overview = [
    ["Temperature", formatNumber(experiment.measuredTemperatureCelsius, " °C")],
    ["Syringe", formatNumber(experiment.syringeConcentrationMicromolar, " µM")],
    ["Cell", formatNumber(experiment.cellConcentrationMicromolar, " µM")],
    ["Cell volume", formatNumber(experiment.cellVolumeMicroliters, " µL")],
    ["Injections", String(experiment.injectionCount)],
    ["Instrument", experiment.instrument || "Unavailable"]
  ];
  $("overview-grid").replaceChildren(...overview.map(([label, value]) => definition(label, value)));
  const experimentComments = $("experiment-comments");
  experimentComments.hidden = !experiment.comments;
  $("experiment-comments-text").textContent = experiment.comments || "";

  const fitSelect = $("fit-select");
  const showFitSelect = experiment.fits.length > 1;
  $("fit-toolbar").hidden = !showFitSelect;
  fitSelect.replaceChildren(...experiment.fits.map((fit, index) => option(index, fit.resultName)));
  if (state.fitIndex >= experiment.fits.length) state.fitIndex = 0;
  fitSelect.value = String(state.fitIndex);

  const orderedViews = ["metadata", "raw", "processed", "fit"];
  const views = orderedViews.filter((name) => experiment.availableViews.includes(name));
  if (!views.includes(state.view))
    state.view = views.includes(state.preferredExperimentView) ? state.preferredExperimentView : "metadata";
  $("tabs").replaceChildren(...views.map((name) => {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = title(name);
    button.setAttribute("aria-selected", String(name === state.view));
    button.addEventListener("click", () => {
      state.view = name;
      state.preferredExperimentView = name;
      renderTabs();
      renderView();
    });
    return button;
  }));
  if (state.workspace === "experiments") renderView();
}

function renderTabs() {
  for (const button of $("tabs").querySelectorAll("button"))
    button.setAttribute("aria-selected", String(button.textContent.toLowerCase() === state.view));
}

function renderView() {
  if (!state.document || state.workspace !== "experiments") return;
  const plot = $("plot");
  const parameters = $("fit-parameters");
  const metadata = $("metadata-grid");
  const message = $("view-message");
  const processedControls = $("processed-controls");
  const fitSummary = $("fit-plot-summary");
  const processingDescription = $("processing-description");
  plot.hidden = state.view === "metadata";
  parameters.hidden = true;
  fitSummary.hidden = true;
  metadata.hidden = state.view !== "metadata";
  processedControls.hidden = state.view !== "processed";
  processingDescription.hidden = state.view !== "processed";
  message.hidden = true;
  window.Plotly?.purge?.(plot);

  if (state.view === "raw") renderRaw(plot, message);
  else if (state.view === "processed") renderProcessed(plot);
  else if (state.view === "fit") renderFitData(plot, parameters, currentExperiment().fits[state.fitIndex], fitSummary);
  else renderMetadata(metadata);
}

function renderResult() {
  if (state.resultIndex >= state.document.analysisResults.length) state.resultIndex = 0;
  const result = currentResult();
  if (!result) return;
  renderResultList();

  $("result-name").textContent = result.name || "Saved analysis result";
  $("result-subtitle").textContent = `${result.modelName || "Unknown model"} · ${result.isGlobal ? "Global" : "Individual"} analysis${result.date ? ` · ${formatDate(result.date)}` : ""}`;
  const summary = [
    ["Model", result.modelName || "Unavailable"],
    ...(result.sequentialSiteCount == null ? [] : [["Binding steps", String(result.sequentialSiteCount)]]),
    ["Experiments", String(result.experimentCount)],
    ["RMSD / loss", formatNumber(result.loss)],
    ["Algorithm", result.solver?.algorithm || "Unavailable"],
    ["Termination", result.solver?.termination || result.solver?.convergence || "Unavailable"],
    ["Date", formatDate(result.date)]
  ];
  $("result-summary-grid").replaceChildren(...summary.map(([label, value]) => definition(label, value)));

  const validity = result.validity || { status: "unknown", reasons: [] };
  const banner = $("result-validity");
  banner.className = `validity-banner validity-${validity.status || "unknown"}`;
  banner.replaceChildren();
  const validityTitle = document.createElement("strong");
  validityTitle.textContent = validityLabel(validity.status);
  banner.append(validityTitle);
  if (validity.reasons?.length) {
    const list = document.createElement("ul");
    validity.reasons.forEach((reason) => { const item = document.createElement("li"); item.textContent = reason; list.append(item); });
    banner.append(list);
  }

  const comments = $("result-comments");
  comments.hidden = !result.comments;
  $("result-comments-text").textContent = result.comments || "";
  const warnings = $("result-warnings");
  warnings.hidden = !result.warnings?.length;
  warnings.textContent = result.warnings?.join(" ") || "";

  renderResultComparison(result);
  renderResultCorrelation(result);
  renderTemperatureParameterEvaluation(result);
  renderAdvancedAnalysis(result);
  const memberSelect = $("result-member-select");
  memberSelect.replaceChildren(...result.members.map((member, index) => option(index, memberLabel(member))));
  if (state.resultMemberIndex >= result.members.length) state.resultMemberIndex = 0;
  memberSelect.value = String(state.resultMemberIndex);
  memberSelect.disabled = result.members.length < 2;
  renderResultMember();
  renderResultDetails(result);
}

function renderAdvancedAnalysis(result) {
  const card = $("result-advanced-card");
  const advanced = result?.advancedAnalyses;
  const available = [
    ["spolarRecord", "Spolar record method", advanced?.spolarRecord, advanced?.spolarRecordUnavailableReason],
    ["electrostatics", "Electrostatics", advanced?.electrostatics, advanced?.electrostaticsUnavailableReason],
    ["protonation", "Protonation", advanced?.protonation, advanced?.protonationUnavailableReason]
  ].filter(([, , value, reason]) => value || reason);
  card.hidden = available.length === 0;
  if (card.hidden) {
    window.Plotly?.purge?.($("advanced-analysis-plot"));
    return;
  }

  if (!available.some(([key]) => key === state.advancedAnalysisKind))
    state.advancedAnalysisKind = available[0][0];
  $("advanced-analysis-tabs").replaceChildren(...available.map(([key, label]) => {
    const button = document.createElement("button");
    button.type = "button";
    button.role = "tab";
    button.textContent = label;
    button.setAttribute("aria-selected", String(key === state.advancedAnalysisKind));
    button.addEventListener("click", () => {
      state.advancedAnalysisKind = key;
      state.advancedPlotKey = null;
      renderAdvancedAnalysis(currentResult());
    });
    return button;
  }));

  const selectedAnalysis = available.find(([key]) => key === state.advancedAnalysisKind);
  const value = selectedAnalysis?.[2];
  const message = $("advanced-analysis-message");
  message.hidden = true;
  message.textContent = "";
  if (!value) {
    renderAdvancedSummary([], []);
    $("advanced-plot-control").hidden = true;
    const target = $("advanced-analysis-plot");
    window.Plotly?.purge?.(target);
    target.hidden = true;
    message.hidden = false;
    message.textContent = "Unavailable";
    return;
  }
  let plots = [];
  let metadataRows = advancedMetadataRows(value?.metadata);
  let parameters = [];
  if (state.advancedAnalysisKind === "spolarRecord") {
    metadataRows = [
      ["Mode", value.foldedMode],
      ["Temperature mode", value.temperatureMode],
      ...metadataRows
    ];
    parameters = [
      advancedParameter("Reference temperature", value.referenceTemperatureCelsius, "°C"),
      advancedParameter("Hydration", value.hydrationContributionKilojoulesPerMole, "kJ/mol"),
      advancedParameter("Conformation", value.conformationalContributionKilojoulesPerMole, "kJ/mol"),
      advancedParameter("Residues", value.residueEstimate, "")
    ];
    if (value.temperatureDependencePlot) plots = [value.temperatureDependencePlot];
  } else if (state.advancedAnalysisKind === "electrostatics") {
    metadataRows = [
      ["Counter-ion iterations", String(value.counterIonReleaseIterations ?? 0)],
      ...metadataRows
    ];
    parameters = [
      advancedParameter("Kd0", value.kd0Micromolar, "µM"),
      advancedParameter("Salt sensitivity", value.saltSensitivity, ""),
      advancedParameter("Curvature", value.curvature, ""),
      advancedParameter("Counter-ion release", value.counterIonRelease, "")
    ];
    plots = value.plots || [];
  } else {
    parameters = [
      advancedParameter("Binding enthalpy", value.bindingEnthalpyKilojoulesPerMole, "kJ/mol"),
      advancedParameter("Protonation change", value.protonationChange, "")
    ];
    if (value.plot) plots = [value.plot];
  }
  renderAdvancedSummary(metadataRows, parameters);

  const control = $("advanced-plot-control");
  const select = $("advanced-plot-select");
  control.hidden = plots.length < 2;
  if (!plots.some((plot) => plot.key === state.advancedPlotKey)) state.advancedPlotKey = plots[0]?.key || null;
  select.replaceChildren(...plots.map((plot) => option(plot.key, plot.title)));
  if (state.advancedPlotKey) select.value = state.advancedPlotKey;
  const plot = plots.find((item) => item.key === state.advancedPlotKey) || plots[0];
  const target = $("advanced-analysis-plot");
  if (!plot || !plot.series?.some((series) => series.x?.length)) {
    window.Plotly?.purge?.(target);
    target.hidden = true;
    message.hidden = false;
    message.textContent = "The saved numeric result is available, but this analysis has no displayable plot data.";
    return;
  }
  target.hidden = false;
  renderAdvancedPlot(target, plot);
}

function advancedMetadataRows(metadata) {
  if (!metadata) return [];
  return [
    ["Completed", formatDate(metadata.completedAtUtc)],
    ["Iterations", String(metadata.completedIterations ?? 0)],
    ["Uncertainty method", metadata.errorEstimationMethod || "Parameter sampling"]
  ];
}

function advancedParameter(label, value, unit) {
  return { label, value, unit };
}

function renderAdvancedSummary(metadataRows, parameters) {
  const metadata = $("advanced-analysis-metadata");
  metadata.hidden = metadataRows.length === 0;
  metadata.replaceChildren(...metadataRows.map(([label, text]) => definition(label, text)));

  const target = $("advanced-analysis-parameter-table");
  target.hidden = parameters.length === 0;
  target.replaceChildren();
  if (target.hidden) return;

  const table = document.createElement("table");
  const head = document.createElement("thead");
  const header = document.createElement("tr");
  ["Parameter", "Value", "SD", "95% interval"].forEach((text) => {
    const th = document.createElement("th");
    th.textContent = text;
    header.append(th);
  });
  head.append(header);

  const body = document.createElement("tbody");
  parameters.forEach((parameter) => {
    const row = document.createElement("tr");
    appendAdvancedCell(row, "Parameter", parameter.label);
    const value = parameter.value;
    if (!value || value.value == null || !Number.isFinite(Number(value.value))) {
      appendAdvancedCell(row, "Value", "Unavailable");
      appendAdvancedCell(row, "SD", "Unavailable");
      appendAdvancedCell(row, "95% interval", "Unavailable");
    } else {
      const suffix = parameter.unit ? ` ${parameter.unit}` : "";
      appendAdvancedCell(row, "Value", formatParameterNumber(value.value, value.sd, suffix));
      appendAdvancedCell(row, "SD", formatParameterNumber(value.sd, value.sd, suffix));
      appendAdvancedCell(row, "95% interval", formatParameterInterval(
        value.confidenceLower, value.confidenceUpper, value.sd, parameter.unit));
    }
    body.append(row);
  });
  table.append(head, body);
  target.append(table);
}

function appendAdvancedCell(row, label, value) {
  const cell = document.createElement("td");
  cell.dataset.label = label;
  cell.textContent = value == null ? "—" : String(value);
  row.append(cell);
}

function renderAdvancedPlot(target, plot) {
  const traces = [];
  const palette = [colors.teal, colors.coral, colors.blue, colors.amber, colors.purple];
  const colorsByGroup = new Map();
  const shownGroups = new Set();
  (plot.series || []).forEach((series, index) => {
    const group = series.group || `series-${index}`;
    if (!colorsByGroup.has(group)) colorsByGroup.set(group, palette[colorsByGroup.size % palette.length]);
    const color = colorsByGroup.get(group);
    const showlegend = !shownGroups.has(group);
    shownGroups.add(group);
    if (series.kind === "points") {
      traces.push({
        x: series.x,
        y: series.y,
        name: series.label,
        type: "scatter",
        mode: "markers",
        legendgroup: group,
        showlegend,
        marker: { color, size: 8 },
        error_y: {
          type: "data",
          symmetric: false,
          array: series.y.map((value, point) => Math.max(0, (series.upper?.[point] ?? value) - value)),
          arrayminus: series.y.map((value, point) => Math.max(0, value - (series.lower?.[point] ?? value))),
          visible: true,
          color
        }
      });
      return;
    }
    if (series.lower?.length === series.x?.length && series.upper?.length === series.x?.length) {
      traces.push({ x: series.x, y: series.lower, type: "scatter", mode: "lines", legendgroup: group, line: { width: 0 }, showlegend: false, hoverinfo: "skip" });
      traces.push({ x: series.x, y: series.upper, type: "scatter", mode: "lines", legendgroup: group, line: { width: 0 }, fill: "tonexty", fillcolor: `${color}22`, showlegend: false, hoverinfo: "skip" });
    }
    traces.push({ x: series.x, y: series.y, name: series.label, type: "scatter", mode: "lines", legendgroup: group, showlegend, line: { color, width: 2 } });
  });
  const layout = baseLayout(plot.xAxisLabel, plot.yAxisLabel);
  window.Plotly.newPlot(target, traces, layout, plotConfig);
}

function renderResultComparison(result) {
  const target = $("result-comparison-plot");
  const message = $("result-comparison-message");
  const memberFits = result.members.map((member) => ({ member, fit: resolveMemberFit(member) }));
  const energyKeys = [];
  memberFits.forEach(({ fit }) => fit?.parameters?.forEach((parameter) => {
    if (isComparableEnergy(parameter) && !energyKeys.includes(parameter.key)) energyKeys.push(parameter.key);
  }));

  if (!energyKeys.length) {
    window.Plotly?.purge?.(target);
    target.hidden = true;
    message.hidden = false;
    message.textContent = "This saved result has no comparable reported energy parameters.";
  } else {
    target.hidden = false;
    message.hidden = true;
    const palette = [colors.teal, colors.coral, colors.blue, colors.amber, colors.purple, colors.raw];
    const experimentLabels = memberFits.map((_, index) => `E${index + 1}`);
    const traces = energyKeys.map((key, index) => {
      const representative = memberFits.map(({ fit }) => fit?.parameters?.find((parameter) => parameter.key === key)).find(Boolean);
      return {
        x: experimentLabels,
        y: memberFits.map(({ fit }) => fit?.parameters?.find((parameter) => parameter.key === key)?.value ?? null),
        error_y: { type: "data", array: memberFits.map(({ fit }) => fit?.parameters?.find((parameter) => parameter.key === key)?.sd ?? 0), visible: true, color: palette[index % palette.length] },
        customdata: memberFits.map(({ member }) => [member.experimentName, member.temperatureCelsius]),
        name: representative?.label || key,
        type: "bar",
        marker: { color: palette[index % palette.length] },
        hovertemplate: "%{customdata[0]}<br>%{y:.5g} " + (representative?.unit || "kJ/mol") + "<br>%{customdata[1]:.3g} °C<extra>%{fullData.name}</extra>"
      };
    });
    const layout = baseLayout("Experiment", "Energy (kJ/mol)");
    layout.barmode = "group";
    layout.bargap = .26;
    layout.bargroupgap = .08;
    layout.height = 470;
    layout.margin.b = 95;
    const categoryCount = memberFits.length;
    layout.xaxis.range = [-.65, Math.max(.65, categoryCount - .35)];
    layout.xaxis.automargin = true;
    const extents = memberFits.flatMap(({ fit }) => energyKeys.flatMap((key) => {
      const parameter = fit?.parameters?.find((item) => item.key === key);
      if (!parameter || !Number.isFinite(Number(parameter.value))) return [];
      const error = Number.isFinite(Number(parameter.sd)) ? Math.abs(Number(parameter.sd)) : 0;
      return [Number(parameter.value) - error, Number(parameter.value) + error];
    }));
    const minimum = Math.min(0, ...extents);
    const maximum = Math.max(0, ...extents);
    const span = maximum - minimum;
    const padding = span > 0 ? span * .1 : Math.max(Math.abs(maximum), 1) * .1;
    layout.yaxis.range = [minimum - padding, maximum + padding];
    layout.yaxis.automargin = true;
    window.Plotly.newPlot(target, traces, layout, plotConfig);
  }

  renderResultParameterTable(result, memberFits);
}

function renderResultCorrelation(result) {
  const card = $("result-correlation-card");
  const selectWrap = $("result-correlation-view-control");
  const select = $("result-correlation-view-select");
  const target = $("result-correlation-plot");
  const scroll = $("result-correlation-plot-scroll");
  const message = $("result-correlation-message");
  const details = $("result-correlation-details");
  const warnings = $("result-correlation-warnings");
  const views = Array.isArray(result?.correlationViews) ? result.correlationViews : [];

  card.hidden = views.length === 0;
  if (card.hidden) {
    window.Plotly?.purge?.(target);
    select.replaceChildren();
    details.replaceChildren();
    warnings.replaceChildren();
    message.hidden = true;
    return;
  }

  const viewKey = (view) => view?.key ?? view?.viewKey ?? view?.id ?? null;
  const savedViewKey = state.correlationViewKeysByResult[result.key];
  const selectedView = views.find((view) => String(viewKey(view)) === String(savedViewKey))
    || views.find((view) => /shared/i.test(String(view?.scope || view?.label || "")))
    || views[0];
  state.correlationViewKeysByResult[result.key] = viewKey(selectedView);

  selectWrap.hidden = views.length < 2;
  select.replaceChildren(...views.map((view, index) => option(
    viewKey(view) ?? index,
    view.label || correlationScopeLabel(view, index))));
  if (state.correlationViewKeysByResult[result.key] != null)
    select.value = String(state.correlationViewKeysByResult[result.key]);

  window.Plotly?.purge?.(target);
  target.hidden = true;
  scroll.hidden = true;
  message.hidden = true;
  message.textContent = "";
  details.replaceChildren();
  warnings.replaceChildren();
  warnings.hidden = true;

  const availability = String(selectedView.status || selectedView.availabilityStatus || selectedView.availability?.status
    || (selectedView.available === false ? "unavailable" : "available")).toLowerCase();
  const parameters = Array.isArray(selectedView.parameters)
    ? selectedView.parameters
    : Array.isArray(selectedView.parameterDescriptors) ? selectedView.parameterDescriptors : [];
  const matrix = normalizeCorrelationMatrix(selectedView.matrix || selectedView.correlationMatrix || selectedView.correlations);
  const available = selectedView.isAvailable === true || selectedView.available === true || availability === "available" || availability === "ok";
  if (!available || parameters.length < 2 || !matrix || matrix.length !== parameters.length || matrix.some((row) => row.length !== parameters.length)) {
    message.hidden = false;
    message.textContent = selectedView.reason || selectedView.message || correlationUnavailableMessage(selectedView, parameters, matrix);
    renderCorrelationDetails(details, selectedView, parameters);
    renderCorrelationWarnings(warnings, selectedView, parameters);
    return;
  }

  renderCorrelationHeatmap(target, scroll, selectedView, parameters, matrix);
  renderCorrelationDetails(details, selectedView, parameters);
  renderCorrelationWarnings(warnings, selectedView, parameters);
}

function correlationScopeLabel(view, index) {
  const scope = String(view?.scope || "").toLowerCase();
  if (scope.includes("shared") && !scope.includes("member")) return "Shared parameters";
  if (scope.includes("single")) return "Single experiment";
  const experiment = view?.experimentName || view?.memberExperimentName || view?.label;
  return experiment ? `Shared + ${experiment} local parameters` : `Correlation scope ${index + 1}`;
}

function normalizeCorrelationMatrix(matrix) {
  if (!Array.isArray(matrix)) return null;
  const rows = matrix.map((row) => Array.isArray(row) ? row.map((value) => Number(value)) : null);
  return rows.some((row) => !row || row.some((value) => !Number.isFinite(value))) ? null : rows;
}

function correlationUnavailableMessage(view, parameters, matrix) {
  const status = String(view?.status || view?.availabilityStatus || view?.availability?.status || "").toLowerCase();
  if (status.includes("residual")) return "Parameter correlation is unavailable because no residual bootstrap was saved.";
  if (status.includes("replicate")) return view.reason || "Parameter correlation is unavailable because there are not enough complete bootstrap replicates.";
  if (status.includes("varying") || parameters.length < 2) return "Parameter correlation is unavailable because fewer than two parameters vary across the saved bootstrap replicates.";
  if (!matrix) return "Parameter correlation is unavailable because the saved matrix is incomplete.";
  return view.reason || "Parameter correlation is unavailable for this saved result.";
}

function renderCorrelationHeatmap(target, scroll, view, parameters, matrix) {
  const compactLabels = parameters.map((parameter) => correlationCompactLabel(parameter));
  const fullLabels = parameters.map((parameter) => parameter.label || parameter.key || "Parameter");
  const n = parameters.length;
  const size = Math.max(420, Math.min(960, n * 54 + 145));
  const width = Math.max(620, n * 58 + 175);
  const text = matrix.map((row) => row.map((value) => Number.isFinite(value) ? value.toFixed(2) : "—"));
  const customdata = matrix.map((row, rowIndex) => row.map((value, columnIndex) => [
    fullLabels[rowIndex],
    fullLabels[columnIndex],
    value
  ]));
  target.hidden = false;
  scroll.hidden = false;
  target.style.width = `${width}px`;
  target.style.height = `${size}px`;
  const layout = {
    autosize: false,
    width,
    height: size,
    margin: { l: 120, r: 30, t: 20, b: 120 },
    paper_bgcolor: "#fff",
    plot_bgcolor: "#fff",
    xaxis: { tickmode: "array", tickvals: compactLabels, ticktext: compactLabels, side: "bottom", tickangle: -45, automargin: true, constrain: "domain" },
    yaxis: { tickmode: "array", tickvals: compactLabels, ticktext: compactLabels, autorange: "reversed", automargin: true, scaleanchor: "x", scaleratio: 1, constrain: "domain" },
    coloraxis: { cmin: -1, cmax: 1, cmid: 0 },
    annotations: []
  };
  window.Plotly.newPlot(target, [{
    type: "heatmap",
    z: matrix,
    x: compactLabels,
    y: compactLabels,
    text,
    texttemplate: "%{text}",
    textfont: { size: n > 12 ? 9 : 11, color: "#182428" },
    customdata,
    hovertemplate: "%{customdata[0]} × %{customdata[1]}<br>r = %{customdata[2]:.4f}<extra></extra>",
    hoverlabel: { align: "left", font: { size: 11 } },
    zmin: -1,
    zmax: 1,
    zmid: 0,
    colorscale: [[0, "#b94b45"], [.5, "#ffffff"], [1, "#386c93"]],
    showscale: true,
    colorbar: { title: "r", thickness: 12, len: .8, tickvals: [-1, 0, 1] }
  }], layout, plotConfig);
}

function correlationCompactLabel(parameter) {
  const prefix = correlationParameterScopePrefix(parameter);
  const key = parameter.label || parameter.key || "Parameter";
  const unlocked = parameter.bootstrapUnlocked || parameter.isBootstrapUnlocked || parameter.unlockedDuringBootstrap;
  return `${prefix} · ${key}${unlocked ? "*" : ""}`;
}

function correlationParameterScopePrefix(parameter) {
  const scope = String(parameter.scope || "").toLowerCase();
  if (scope.includes("member") || scope.includes("local")) return "L";
  if (scope.includes("single")) return "S";
  return "S";
}

function renderCorrelationDetails(target, view, parameters) {
  const availability = view?.availability || {};
  const rows = [
    ["Method", view?.method || "Residual bootstrap (Pearson)"],
    ["Scope", view?.label || correlationScopeLabel(view, 0)],
    ["Complete replicates", formatCount(view?.usedReplicates ?? view?.usedReplicateCount ?? view?.completeReplicates ?? availability.completeReplicates ?? availability.completeReplicateCount)],
    ["Required replicates", formatCount(view?.requiredReplicates ?? view?.requiredReplicateCount ?? view?.minimumCompleteReplicates ?? availability.requiredReplicates ?? availability.minimumCompleteReplicates)],
    ["Varying parameters", formatCount(view?.varyingParameterCount ?? view?.varyingParameters ?? availability.varyingParameters ?? availability.varyingParameterCount ?? parameters.length)],
    ["Omitted parameters", formatCount(view?.omittedParameterCount ?? view?.omittedParameters?.length)]
  ];
  target.replaceChildren(...rows.map(([label, value]) => definition(label, value)));
}

function formatCount(value) { return value == null || !Number.isFinite(Number(value)) ? "Unavailable" : String(Number(value)); }

function renderCorrelationWarnings(target, view, parameters) {
  const warningItems = [];
  const bootstrapWarnings = view?.bootstrapUnlockedWarnings || view?.bootstrapUnlockedWarning || view?.warnings;
  if (Array.isArray(bootstrapWarnings)) warningItems.push(...bootstrapWarnings);
  else if (bootstrapWarnings) warningItems.push(String(bootstrapWarnings));
  if ((view?.hasBootstrapUnlockedParameters || parameters.some((parameter) => parameter.bootstrapUnlocked || parameter.isBootstrapUnlocked || parameter.unlockedDuringBootstrap))
    && !warningItems.some((warning) => /unlock|originally locked/i.test(String(warning))))
    warningItems.push("Some parameters were unlocked during bootstrap to estimate their correlation.");
  const rankWarning = view?.rankLimitedWarning || view?.rankLimitedCovarianceWarning || view?.rankLimitedCovariance || view?.rankLimited || view?.isRankLimited;
  if (rankWarning && !warningItems.some((warning) => /rank/i.test(String(warning))))
    warningItems.push(typeof rankWarning === "string" ? rankWarning : "The covariance estimate was limited by the available bootstrap rank.");
  const uniqueWarnings = [...new Set(warningItems.map((warning) => String(warning)).filter(Boolean))];
  target.hidden = uniqueWarnings.length === 0;
  target.replaceChildren(...uniqueWarnings.map((warning) => {
    const item = document.createElement("li");
    item.textContent = warning;
    return item;
  }));
}

function renderResultParameterTable(result, memberFits) {
  const target = $("result-parameter-table");
  const parameterKeys = [];
  memberFits.forEach(({ fit }) => fit?.parameters?.forEach((parameter) => {
    if (!parameterKeys.includes(parameter.key)) parameterKeys.push(parameter.key);
  }));
  const parameterByKey = new Map();
  memberFits.forEach(({ fit }) => fit?.parameters?.forEach((parameter) => parameterByKey.set(parameter.key, parameter)));

  const table = document.createElement("table");
  const head = document.createElement("thead");
  const header = document.createElement("tr");
  ["Experiment", "Temperature", "Member loss", ...parameterKeys.map((key) => parameterByKey.get(key)?.label || key)].forEach((text) => {
    const th = document.createElement("th");
    th.textContent = text;
    header.append(th);
  });
  head.append(header);
  const body = document.createElement("tbody");
  memberFits.forEach(({ member, fit }) => {
    const row = document.createElement("tr");
    appendCell(row, member.experimentName, "experiment-name-cell");
    appendCell(row, formatNumber(member.temperatureCelsius, " °C"));
    appendCell(row, formatNumber(member.loss));
    parameterKeys.forEach((key) => appendParameterCell(row, fit?.parameters?.find((parameter) => parameter.key === key)));
    body.append(row);
  });
  table.append(head, body);
  target.replaceChildren(table);
}

function renderTemperatureParameterEvaluation(result) {
  const card = $("result-temperature-evaluation-card");
  const input = $("result-evaluation-temperature");
  const note = $("temperature-evaluation-note");
  const message = $("result-temperature-evaluation-message");
  const target = $("result-temperature-evaluation-table");
  const evaluation = result?.temperatureParameterEvaluation;
  card.hidden = !evaluation?.dependences?.length;
  if (card.hidden) return;

  if (!Number.isFinite(state.resultEvaluationTemperature))
    state.resultEvaluationTemperature = roundTemperatureToHalf(evaluation.defaultTemperatureCelsius);
  input.value = formatInputNumber(state.resultEvaluationTemperature);
  const range = evaluation.minimumTemperatureCelsius != null && evaluation.maximumTemperatureCelsius != null
    ? `Saved experiments span ${formatNumber(evaluation.minimumTemperatureCelsius, " °C")} to ${formatNumber(evaluation.maximumTemperatureCelsius, " °C")}.`
    : "";
  note.textContent = evaluation.isTemperatureDependent
    ? `Evaluated from the saved global temperature dependence. ${range}`
    : `The saved fit has no resolved temperature dependence; reported energy terms remain constant. ${range}`;

  if (!Number.isFinite(state.resultEvaluationTemperature) || state.resultEvaluationTemperature < -273.15) {
    message.hidden = false;
    message.textContent = "Enter a temperature at or above −273.15 °C.";
    target.replaceChildren();
    return;
  }
  message.hidden = true;
  const termKey = (item) => `${item.family}:${item.slotIndex}`;
  const terms = new Map(evaluation.dependences.map((item) => [termKey(item), evaluateTemperatureDependence(item, state.resultEvaluationTemperature)]));
  const rows = [];
  const slots = [...new Set(evaluation.dependences.map((item) => Number(item.slotIndex)).filter(Number.isFinite))].sort((a, b) => a - b);
  const includeIndex = slots.length > 1;
  slots.forEach((slot) => {
    const suffix = includeIndex ? ` ${slot}` : "";
    addTemperatureEvaluationRow(rows, terms.get(`Enthalpy:${slot}`), `Enthalpy${suffix}`, "kJ/mol");
    addTemperatureEvaluationRow(rows, terms.get(`EntropyContribution:${slot}`), `Entropy contribution${suffix}`, "kJ/mol");
    const gibbs = terms.get(`Gibbs:${slot}`);
    addTemperatureEvaluationRow(rows, gibbs, `Gibbs free energy${suffix}`, "kJ/mol");
    const affinity = deriveAffinity(gibbs, state.resultEvaluationTemperature);
    addTemperatureEvaluationRow(rows, affinity, `Affinity${suffix}`, affinity?.unit || "µM");
    const enthalpy = evaluation.dependences.find((item) => item.family === "Enthalpy" && item.slotIndex === slot);
    if (enthalpy && Math.abs(enthalpy.slope.value) > 1e-12)
      addTemperatureEvaluationRow(rows, enthalpy.slope, `Heat capacity change${suffix}`, "kJ/(mol·K)");
  });

  const table = document.createElement("table");
  const head = document.createElement("thead");
  const header = document.createElement("tr");
  ["Parameter", "Value", "SD", "95% interval"].forEach((text) => { const th = document.createElement("th"); th.textContent = text; header.append(th); });
  head.append(header);
  const body = document.createElement("tbody");
  rows.forEach((item) => {
    const row = document.createElement("tr");
    appendCell(row, item.label);
    appendCell(row, formatParameterNumber(item.value, item.sd, ` ${item.unit}`));
    appendCell(row, formatParameterNumber(item.sd, item.sd, ` ${item.unit}`));
    appendCell(row, formatParameterInterval(item.confidenceLower, item.confidenceUpper, item.sd, item.unit));
    body.append(row);
  });
  table.append(head, body);
  target.replaceChildren(table);
}

function evaluateTemperatureDependence(dependence, temperatureCelsius) {
  const delta = temperatureCelsius - dependence.referenceTemperatureCelsius;
  const intercept = dependence.intercept;
  const slope = dependence.slope;
  const value = intercept.value + delta * slope.value;
  const sd = Math.hypot(intercept.sd || 0, delta * (slope.sd || 0));
  return {
    value,
    sd,
    confidenceLower: value - 1.96 * sd,
    confidenceUpper: value + 1.96 * sd
  };
}

function deriveAffinity(gibbs, temperatureCelsius) {
  if (!gibbs || temperatureCelsius <= -273.15) return null;
  const factor = 1000 / (8.3145 * (temperatureCelsius + 273.15));
  const convert = (value) => Number.isFinite(value) ? Math.exp(value * factor) : null;
  const value = convert(gibbs.value);
  if (!Number.isFinite(value)) return null;
  const concentration = concentrationDisplayScale(value);
  return {
    value: value * concentration.scale,
    sd: Math.abs(value * factor * (gibbs.sd || 0)) * concentration.scale,
    confidenceLower: Number.isFinite(convert(gibbs.confidenceLower)) ? convert(gibbs.confidenceLower) * concentration.scale : null,
    confidenceUpper: Number.isFinite(convert(gibbs.confidenceUpper)) ? convert(gibbs.confidenceUpper) * concentration.scale : null,
    unit: concentration.unit
  };
}

function concentrationDisplayScale(valueMolar) {
  const magnitude = Math.log10(Math.abs(valueMolar));
  if (!Number.isFinite(magnitude)) return { scale: 1e6, unit: "µM" };
  if (magnitude > 0) return { scale: 1, unit: "M" };
  if (magnitude > -3) return { scale: 1e3, unit: "mM" };
  if (magnitude > -6) return { scale: 1e6, unit: "µM" };
  if (magnitude > -9) return { scale: 1e9, unit: "nM" };
  return { scale: 1e12, unit: "pM" };
}

function addTemperatureEvaluationRow(rows, value, label, unit) {
  if (value && Number.isFinite(value.value)) rows.push({ ...value, label, unit });
}

function renderResultMember() {
  const result = currentResult();
  if (!result) return;
  const member = result.members[state.resultMemberIndex];
  const target = $("result-fit-plot");
  const parameterBox = $("result-fit-parameters");
  const fitSummary = $("result-fit-plot-summary");
  const message = $("result-member-message");
  window.Plotly?.purge?.(target);
  parameterBox.hidden = true;
  parameterBox.replaceChildren();
  fitSummary.hidden = true;

  const fit = member && resolveMemberFit(member);
  if (!member || !fit) {
    target.hidden = true;
    message.hidden = false;
    message.textContent = member?.availabilityMessage || "The saved fit for this result member is unavailable.";
    return;
  }
  target.hidden = false;
  message.hidden = true;
  renderFitData(target, parameterBox, fit, fitSummary);
}

function renderResultDetails(result) {
  const solver = result.solver || {};
  const solverRows = [
    ["Algorithm", solver.algorithm],
    ["Termination", solver.termination],
    ["Convergence", solver.convergence],
    ["Iterations", solver.iterations == null ? null : String(solver.iterations)],
    ["Weighted fitting", solver.weightedFitting ? "Yes" : "No"],
    ["Error method", solver.errorEstimationMethod],
    ["Error summary", solver.errorEstimationSummary],
    ["Bootstrap samples", String(solver.bootstrapIterations ?? 0)],
    ["Elapsed time", solver.elapsedSeconds == null ? null : `${formatNumber(solver.elapsedSeconds)} s`]
  ];
  $("result-solver-details").replaceChildren(...solverRows.map(([label, value]) => definition(label, value || "Unavailable")));
  renderSettings($("result-model-options"), result.modelOptions, "No saved model options.");
  renderSettings($("result-constraints"), result.constraints, "No active parameter constraints.");
}

function renderSettings(target, settings, emptyMessage) {
  if (!settings?.length) {
    target.replaceChildren(definition("Status", emptyMessage));
    return;
  }
  target.replaceChildren(...settings.map((setting) => definition(setting.label || setting.key, setting.value)));
}

function setProcessingMode(mode) {
  if (mode !== "raw" && mode !== "corrected") return;
  state.processingMode = mode;
  $("processed-mode-raw").setAttribute("aria-pressed", String(mode === "raw"));
  $("processed-mode-corrected").setAttribute("aria-pressed", String(mode === "corrected"));
  if (state.view === "processed" && state.workspace === "experiments") {
    window.Plotly?.purge?.($("plot"));
    renderProcessed($("plot"));
  }
}

function toggleIntegrationRanges() {
  state.showIntegrationRanges = !state.showIntegrationRanges;
  $("processed-integration-ranges").setAttribute("aria-pressed", String(state.showIntegrationRanges));
  if (state.view === "processed" && state.workspace === "experiments") {
    window.Plotly?.purge?.($("plot"));
    renderProcessed($("plot"));
  }
}

function renderRaw(target, message) {
  const raw = currentExperiment().raw;
  if (raw.unavailableChannels?.length) {
    message.hidden = false;
    message.textContent = raw.unavailableChannels.join(" ");
  }
  const traces = [{ x: raw.timeSeconds, y: raw.powerMicrowatts, name: "Power", type: "scatter", mode: "lines", line: { color: colors.teal, width: 1.2 }, hovertemplate: "%{x:.2f} s<br>%{y:.4g} µW<extra>Power</extra>" }];
  const layout = baseLayout("Time (s)", "Power (µW)");
  if (raw.temperatureCelsius) {
    traces.push({ x: raw.timeSeconds, y: raw.temperatureCelsius, name: "Temperature", type: "scatter", mode: "lines", yaxis: "y2", line: { color: colors.coral, width: 1 }, hovertemplate: "%{x:.2f} s<br>%{y:.3f} °C<extra>Temperature</extra>" });
    layout.yaxis2 = { title: "Temperature (°C)", overlaying: "y", side: "right", showgrid: false };
  }
  layout.shapes = raw.injectionTimesSeconds.map((time) => ({ type: "line", x0: time, x1: time, y0: 0, y1: 1, yref: "paper", line: { color: "rgba(100,114,119,.28)", width: 1 } }));
  window.Plotly.newPlot(target, traces, layout, plotConfig);
}

function renderProcessed(target) {
  const experiment = currentExperiment();
  const data = experiment.processed;
  const corrected = state.processingMode === "corrected";
  $("processed-mode-raw").setAttribute("aria-pressed", String(!corrected));
  $("processed-mode-corrected").setAttribute("aria-pressed", String(corrected));
  const traces = corrected
    ? [
        { x: data.timeSeconds, y: data.correctedPowerMicrowatts, name: "Corrected", type: "scatter", mode: "lines", line: { color: colors.teal, width: 1.2 }, hovertemplate: "%{x:.2f} s<br>%{y:.4g} µW<extra>Corrected</extra>" },
        { x: data.timeSeconds, y: data.timeSeconds.map(() => 0), name: "Baseline = 0", type: "scatter", mode: "lines", line: { color: colors.amber, width: 1.2, dash: "dash" }, hovertemplate: "%{x:.2f} s<br>0 µW<extra>Baseline</extra>" }
      ]
    : [
        { x: data.timeSeconds, y: data.rawPowerMicrowatts, name: "Raw", type: "scatter", mode: "lines", line: { color: colors.raw, width: 1 }, hovertemplate: "%{x:.2f} s<br>%{y:.4g} µW<extra>Raw</extra>" },
        { x: data.timeSeconds, y: data.baselinePowerMicrowatts, name: `Baseline (${data.baselineMethod})`, type: "scatter", mode: "lines", line: { color: colors.amber, width: 1.4 }, hovertemplate: "%{x:.2f} s<br>%{y:.4g} µW<extra>Baseline</extra>" }
      ];
  if (!corrected && data.controlPointTimesSeconds?.length)
    traces.push({ x: data.controlPointTimesSeconds, y: data.controlPointPowerMicrowatts, name: "Baseline points", type: "scatter", mode: "markers", marker: { color: colors.amber, size: 7 } });

  const plottedValues = corrected
    ? data.correctedPowerMicrowatts
    : data.rawPowerMicrowatts.concat(data.baselinePowerMicrowatts, data.controlPointPowerMicrowatts || []);
  const rangeTraces = integrationRangeTraces(data, experiment.integrated, plottedValues);
  const rangeButton = $("processed-integration-ranges");
  const rangeLabel = $("processed-integration-ranges-label");
  rangeButton.hidden = rangeTraces.length === 0;
  rangeLabel.hidden = rangeTraces.length === 0;
  rangeButton.setAttribute("aria-pressed", String(state.showIntegrationRanges));

  const layout = baseLayout("Time (s)", "Power (µW)");
  if (state.showIntegrationRanges && rangeTraces.length) traces.unshift(...rangeTraces);
  window.Plotly.newPlot(target, traces, layout, plotConfig);
}

function integrationRangeTraces(data, integrated, plottedValues) {
  const starts = data.integrationStartSeconds || [];
  const ends = data.integrationEndSeconds || [];
  const count = Math.min(starts.length, ends.length);
  let minimumY = Infinity;
  let maximumY = -Infinity;
  for (const rawValue of plottedValues || []) {
    const value = Number(rawValue);
    if (!Number.isFinite(value)) continue;
    minimumY = Math.min(minimumY, value);
    maximumY = Math.max(maximumY, value);
  }
  if (!Number.isFinite(minimumY) || !Number.isFinite(maximumY)) {
    minimumY = -1;
    maximumY = 1;
  }
  if (minimumY === maximumY) {
    const padding = Math.max(Math.abs(minimumY) * .05, 1);
    minimumY -= padding;
    maximumY += padding;
  }
  const styles = {
    included: { name: "Included integration range", fill: "rgba(8,126,120,.09)", line: "rgba(8,126,120,.38)", dash: "solid" },
    excluded: { name: "Excluded integration range", fill: "rgba(113,128,134,.08)", line: "rgba(113,128,134,.45)", dash: "dash" },
    unavailable: { name: "Not integrated", fill: "rgba(193,139,41,.08)", line: "rgba(193,139,41,.5)", dash: "dot" }
  };
  const polygons = {
    included: { x: [], y: [] },
    excluded: { x: [], y: [] },
    unavailable: { x: [], y: [] }
  };

  for (let index = 0; index < count; index++) {
    const start = Number(starts[index]);
    const end = Number(ends[index]);
    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) continue;

    const kind = integrated?.isIntegrated?.[index] === false
      ? "unavailable"
      : integrated?.included?.[index] === false ? "excluded" : "included";
    polygons[kind].x.push(start, start, end, end, start, null);
    polygons[kind].y.push(minimumY, maximumY, maximumY, minimumY, minimumY, null);
  }

  return Object.entries(polygons)
    .filter(([, polygon]) => polygon.x.length > 0)
    .map(([kind, polygon]) => ({
      x: polygon.x,
      y: polygon.y,
      name: styles[kind].name,
      legendgroup: `integration-${kind}`,
      type: "scatter",
      mode: "lines",
      fill: "toself",
      fillcolor: styles[kind].fill,
      line: { color: styles[kind].line, width: 1, dash: styles[kind].dash },
      hoverinfo: "skip"
    }));
}

function renderFitData(target, parameterBox, fit, summaryBox) {
  if (!fit) return;
  const order = fit.x.map((value, index) => [value, index]).sort((a, b) => a[0] - b[0]).map((pair) => pair[1]);
  const confidenceBand = buildConfidenceBand(fit, order);
  summaryBox.hidden = false;
  summaryBox.replaceChildren(
    definition("Model", fit.modelName || "Unavailable"),
    definition("RMSD / loss", formatNumber(fit.loss)),
    definition("Confidence band", confidenceBand.available ? "95% bootstrap confidence" : "Bootstrap interval unavailable")
  );
  const included = indices(fit.included, true);
  const excluded = indices(fit.included, false);
  const xValues = fit.x.filter((value) => Number.isFinite(Number(value)) && Number(value) >= 0).map(Number);
  const maximumX = xValues.length ? Math.max(...xValues) : 1;
  const xRange = [0, maximumX > 0 ? maximumX * 1.04 : 1];
  const traces = [];
  if (confidenceBand.available) {
    traces.push({ x: confidenceBand.points.map((point) => point.x), y: confidenceBand.points.map((point) => point.lower), type: "scatter", mode: "lines", line: { width: 0 }, hoverinfo: "skip", showlegend: false, connectgaps: false });
    traces.push({ x: confidenceBand.points.map((point) => point.x), y: confidenceBand.points.map((point) => point.upper), type: "scatter", mode: "lines", line: { width: 0 }, fill: "tonexty", fillcolor: colors.pale, name: "95% bootstrap confidence", hoverinfo: "skip", connectgaps: false });
  }
  traces.push({ x: order.map((i) => fit.x[i]), y: order.map((i) => fit.fittedKilojoulesPerMole[i]), name: "Fit", type: "scatter", mode: "lines", line: { color: colors.coral, width: 2 } });
  traces.push(fitPointTrace(included, "Included", colors.teal, "circle"));
  if (excluded.length) traces.push(fitPointTrace(excluded, "Excluded", colors.raw, "circle-open"));
  traces.push({ x: included.map((i) => fit.x[i]), y: included.map((i) => fit.residualKilojoulesPerMole[i]), name: "Residual", type: "scatter", mode: "markers", marker: { color: colors.teal, size: 7 }, yaxis: "y2", showlegend: false, hovertemplate: "%{x:.5g}<br>%{y:.5g} kJ/mol<extra>Residual</extra>" });

  const axisTitle = fit.analysisXAxisUnit ? `${fit.analysisXAxisName} (${fit.analysisXAxisUnit})` : fit.analysisXAxisName;
  const layout = baseLayout("", "Observed heat (kJ/mol)");
  layout.xaxis = { domain: [0, 1], anchor: "y2", range: xRange, minallowed: 0, title: axisTitle };
  layout.yaxis = { domain: [.34, 1], title: "Observed heat (kJ/mol)", zeroline: true };
  layout.yaxis2 = { domain: [0, .22], title: "Residual (kJ/mol)", zeroline: true };
  layout.height = 660;
  window.Plotly.newPlot(target, traces, layout, plotConfig);
  renderParameters(parameterBox, fit);

  function fitPointTrace(ids, name, color, symbol) {
    return { x: ids.map((i) => fit.x[i]), y: ids.map((i) => fit.observedKilojoulesPerMole[i]), error_y: { type: "data", array: ids.map((i) => fit.observationSdKilojoulesPerMole[i] || 0), visible: true, color }, name, type: "scatter", mode: "markers", marker: { color, symbol, size: 9 }, hovertemplate: "%{x:.5g}<br>%{y:.5g} kJ/mol<extra>" + name + "</extra>" };
  }
}

function buildConfidenceBand(fit, order) {
  const lower = fit?.confidenceLowerKilojoulesPerMole;
  const upper = fit?.confidenceUpperKilojoulesPerMole;
  if (!Array.isArray(lower) || !Array.isArray(upper) || lower.length !== upper.length || lower.length !== (fit?.x?.length || 0))
    return { available: false, points: [] };

  const points = order.map((index) => {
    const x = Number(fit.x[index]);
    const rawLower = lower[index];
    const rawUpper = upper[index];
    const lo = Number(rawLower);
    const hi = Number(rawUpper);
    return rawLower != null && rawUpper != null && Number.isFinite(x) && Number.isFinite(lo) && Number.isFinite(hi) && hi > lo
      ? { x, lower: lo, upper: hi }
      : null;
  }).filter(Boolean);
  return { available: points.length > 0, points };
}

function renderParameters(target, fit) {
  target.hidden = false;
  const fitted = fit.parameters.filter((parameter) => !parameter.isDerived);
  const derived = fit.parameters.filter((parameter) => parameter.isDerived);
  const sections = [parameterSection("Fitted parameters", fitted, true)];
  if (derived.length) sections.push(parameterSection("Derived parameters", derived, false));
  target.replaceChildren(...sections);
}

function parameterSection(titleText, parameters, showStatus) {
  const section = document.createElement("section");
  section.className = "parameter-section";
  const heading = document.createElement("h3");
  heading.textContent = titleText;
  const table = document.createElement("table");
  const head = document.createElement("thead");
  const row = document.createElement("tr");
  ["Parameter", "Value", "SD", "95% interval", ...(showStatus ? ["Status"] : [])]
    .forEach((text) => { const th = document.createElement("th"); th.textContent = text; row.append(th); });
  head.append(row);
  const body = document.createElement("tbody");
  parameters.forEach((parameter) => {
    const tr = document.createElement("tr");
    const unit = parameter.unit ? ` ${parameter.unit}` : "";
    const values = [parameter.label, formatParameterNumber(parameter.value, parameter.sd, unit), formatParameterNumber(parameter.sd, parameter.sd, unit), formatParameterInterval(parameter.confidenceLower, parameter.confidenceUpper, parameter.sd, parameter.unit)];
    values.forEach((value) => appendCell(tr, value));
    if (showStatus) appendParameterStatusCell(tr, parameter);
    body.append(tr);
  });
  table.append(head, body);
  section.append(heading, table);
  return section;
}

function renderMetadata(target) {
  const experiment = currentExperiment();
  target.style.display = "grid";
  target.replaceChildren(...experiment.metadata.map((item) =>
    definition(item.label, item.label === "Date" ? formatDate(experiment.date) : item.value)));
}

function resolveMemberFit(member) {
  if (!member?.experimentKey || !member?.fitKey) return null;
  const experiment = state.document.experiments.find((item) => item.key === member.experimentKey);
  return experiment?.fits?.find((fit) => fit.key === member.fitKey && fit.resultKey === currentResult()?.key) || null;
}

function currentExperiment() { return state.document?.experiments?.[state.experimentIndex]; }
function currentResult() { return state.document?.analysisResults?.[state.resultIndex]; }
function memberLabel(member) { return `${member.experimentName}${member.temperatureCelsius == null ? "" : ` · ${formatNumber(member.temperatureCelsius)} °C`}`; }
function option(value, label) { const element = document.createElement("option"); element.value = String(value); element.textContent = label; return element; }
function definition(label, value) { const wrapper = document.createElement("div"); const dt = document.createElement("dt"); const dd = document.createElement("dd"); dt.textContent = label; dd.textContent = value || "Unavailable"; wrapper.append(dt, dd); return wrapper; }
function appendCell(row, value, className = "") { const cell = document.createElement("td"); cell.className = className; cell.textContent = value == null ? "—" : String(value); row.append(cell); }
function appendParameterCell(row, parameter) {
  const cell = document.createElement("td");
  if (!parameter) { cell.textContent = "—"; row.append(cell); return; }
  const value = document.createElement("span");
  const unit = parameter.unit ? ` ${parameter.unit}` : "";
  value.textContent = formatParameterNumber(parameter.value, parameter.sd, unit);
  cell.append(value);
  const details = [];
  if (Number.isFinite(Number(parameter.sd))) details.push(`SD ${formatParameterNumber(parameter.sd, parameter.sd)}`);
  if (parameter.confidenceLower != null) details.push(`95% ${formatParameterInterval(parameter.confidenceLower, parameter.confidenceUpper, parameter.sd, "")}`);
  if (parameter.isLocked) details.push("Locked");
  else if (parameter.isGloballyDetermined) details.push("Globally constrained");
  else if (parameter.isDerived) details.push("Derived");
  if (details.length) { const small = document.createElement("small"); small.textContent = details.join(" · "); cell.append(document.createElement("br"), small); }
  row.append(cell);
}
function appendParameterStatusCell(row, parameter) {
  const cell = document.createElement("td");
  const status = document.createElement("span");
  status.className = `parameter-status${parameter.isLocked ? " parameter-status-locked" : ""}`;
  status.textContent = parameter.isLocked ? "Locked" : parameter.isGloballyDetermined ? "Globally constrained" : "Fitted";
  cell.append(status);
  row.append(cell);
}
function indices(values, expected) { return values.map((value, index) => value === expected ? index : -1).filter((index) => index >= 0); }
function isComparableEnergy(parameter) { return parameter?.unit === "kJ/mol" && /enthalpy|gibbs|entropy/i.test(`${parameter.key} ${parameter.label}`); }
function validityLabel(status) { return ({ valid: "Valid saved result", partialInvalid: "Partially valid saved result", invalid: "Invalid saved result", unknown: "Validity not recorded" })[status] || "Validity not recorded"; }
function title(value) { return value.charAt(0).toUpperCase() + value.slice(1); }
function formatNumber(value, suffix = "") { return value == null || !Number.isFinite(Number(value)) ? "Unavailable" : `${numberFormatter.format(Number(value))}${suffix}`; }
function parameterFractionDigits(sd) {
  const magnitude = Math.abs(Number(sd));
  if (!Number.isFinite(magnitude) || magnitude <= 0) return null;
  return Math.max(0, 1 - Math.floor(Math.log10(magnitude)));
}
function formatDecimal(value, fractionDigits) {
  if (fractionDigits == null) return numberFormatter.format(Number(value));
  if (fractionDigits > 20) return Number(value).toExponential(5).replace(/\.?(?:0+)(?=e)/, "");
  return new Intl.NumberFormat("en-US", { useGrouping: false, minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits }).format(Number(value));
}
function formatParameterNumber(value, sd, suffix = "") {
  if (value == null || !Number.isFinite(Number(value))) return "Unavailable";
  const digits = parameterFractionDigits(sd);
  return `${digits == null ? numberFormatter.format(Number(value)) : formatDecimal(Number(value), digits)}${suffix}`;
}
function formatParameterInterval(lower, upper, sd, unit = "") {
  if (lower == null || upper == null || !Number.isFinite(Number(lower)) || !Number.isFinite(Number(upper))) return "—";
  const suffix = unit ? ` ${unit}` : "";
  return `${formatParameterNumber(lower, sd)} – ${formatParameterNumber(upper, sd)}${suffix}`;
}
function formatInputNumber(value) { return Number.isFinite(Number(value)) ? String(Number(value)) : ""; }
function roundTemperatureToHalf(value) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? Math.round(numeric * 2) / 2 : value;
}
function formatDate(value) {
  if (!value) return "Unavailable";
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return "Unavailable";
  const pad = (part) => String(part).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
function formatBytes(bytes) { if (bytes < 1024) return `${bytes} B`; if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`; return `${(bytes / 1024 / 1024).toFixed(1)} MB`; }
function baseLayout(xTitle, yTitle) { return { autosize: true, height: 540, margin: { l: 70, r: 65, t: 35, b: 60 }, paper_bgcolor: "#fff", plot_bgcolor: "#fff", hovermode: "closest", legend: { orientation: "h", y: 1.08 }, xaxis: { title: xTitle, gridcolor: "#edf1f1" }, yaxis: { title: yTitle, gridcolor: "#edf1f1", zerolinecolor: "#bdcaca" } }; }
