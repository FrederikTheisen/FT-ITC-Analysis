const state = {
  token: null,
  document: null,
  workspace: "experiments",
  experimentIndex: 0,
  view: "metadata",
  processingMode: "raw",
  fitIndex: 0,
  resultIndex: 0,
  resultMemberIndex: 0
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
    $("file-label").textContent = event.target.files[0]?.name || "Choose a .ftitc or .itc file";
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
  $("processed-mode-raw").addEventListener("click", () => setProcessingMode("raw"));
  $("processed-mode-corrected").addEventListener("click", () => setProcessingMode("corrected"));
}

async function refreshToken() {
  const response = await fetch("/api/viewer/token", { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) throw new Error("Could not initialize secure uploads.");
  state.token = (await response.json()).requestToken;
}

async function openFile(event) {
  event.preventDefault();
  const file = $("file-input").files[0];
  if (!file) return showError("Choose a .ftitc or .itc file.");
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
    state.processingMode = "raw";
    state.resultIndex = 0;
    state.resultMemberIndex = 0;
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
  $("viewer").hidden = true;
  $("upload-panel").hidden = false;
  $("upload-form").reset();
  $("file-label").textContent = "Choose a .ftitc or .itc file";
  ["plot", "result-comparison-plot", "result-fit-plot"].forEach((id) => window.Plotly?.purge?.($(id)));
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
    window.Plotly?.purge?.($("result-fit-plot"));
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
      state.view = "metadata";
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
  if (!views.includes(state.view)) state.view = "metadata";
  $("tabs").replaceChildren(...views.map((name) => {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = title(name);
    button.setAttribute("aria-selected", String(name === state.view));
    button.addEventListener("click", () => { state.view = name; renderTabs(); renderView(); });
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
  plot.hidden = state.view === "metadata";
  parameters.hidden = true;
  fitSummary.hidden = true;
  metadata.hidden = state.view !== "metadata";
  processedControls.hidden = state.view !== "processed";
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
  const memberSelect = $("result-member-select");
  memberSelect.replaceChildren(...result.members.map((member, index) => option(index, memberLabel(member))));
  if (state.resultMemberIndex >= result.members.length) state.resultMemberIndex = 0;
  memberSelect.value = String(state.resultMemberIndex);
  memberSelect.disabled = result.members.length < 2;
  renderResultMember();
  renderResultDetails(result);
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
    appendCell(row, member.experimentName);
    appendCell(row, formatNumber(member.temperatureCelsius, " °C"));
    appendCell(row, formatNumber(member.loss));
    parameterKeys.forEach((key) => appendParameterCell(row, fit?.parameters?.find((parameter) => parameter.key === key)));
    body.append(row);
  });
  table.append(head, body);
  target.replaceChildren(table);
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
  const data = currentExperiment().processed;
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
  window.Plotly.newPlot(target, traces, baseLayout("Time (s)", "Power (µW)"), plotConfig);
}

function renderFitData(target, parameterBox, fit, summaryBox) {
  if (!fit) return;
  summaryBox.hidden = false;
  summaryBox.replaceChildren(
    definition("Model", fit.modelName || "Unavailable"),
    definition("RMSD / loss", formatNumber(fit.loss))
  );
  const included = indices(fit.included, true);
  const excluded = indices(fit.included, false);
  const order = fit.x.map((value, index) => [value, index]).sort((a, b) => a[0] - b[0]).map((pair) => pair[1]);
  const traces = [];
  if (fit.confidenceLowerKilojoulesPerMole?.some((value) => value != null)) {
    traces.push({ x: order.map((i) => fit.x[i]), y: order.map((i) => fit.confidenceLowerKilojoulesPerMole[i]), type: "scatter", mode: "lines", line: { width: 0 }, hoverinfo: "skip", showlegend: false });
    traces.push({ x: order.map((i) => fit.x[i]), y: order.map((i) => fit.confidenceUpperKilojoulesPerMole[i]), type: "scatter", mode: "lines", line: { width: 0 }, fill: "tonexty", fillcolor: colors.pale, name: "95% confidence", hoverinfo: "skip" });
  }
  traces.push({ x: order.map((i) => fit.x[i]), y: order.map((i) => fit.fittedKilojoulesPerMole[i]), name: "Fit", type: "scatter", mode: "lines", line: { color: colors.coral, width: 2 } });
  traces.push(fitPointTrace(included, "Included", colors.teal, "circle"));
  if (excluded.length) traces.push(fitPointTrace(excluded, "Excluded", colors.raw, "circle-open"));
  traces.push({ x: included.map((i) => fit.x[i]), y: included.map((i) => fit.residualKilojoulesPerMole[i]), name: "Residual", type: "scatter", mode: "markers", marker: { color: colors.teal, size: 7 }, xaxis: "x2", yaxis: "y2", showlegend: false, hovertemplate: "%{x:.5g}<br>%{y:.5g} kJ/mol<extra>Residual</extra>" });

  const axisTitle = fit.analysisXAxisUnit ? `${fit.analysisXAxisName} (${fit.analysisXAxisUnit})` : fit.analysisXAxisName;
  const layout = baseLayout("", "Observed heat (kJ/mol)");
  layout.xaxis = { domain: [0, 1], anchor: "y", showticklabels: false };
  layout.yaxis = { domain: [.34, 1], title: "Observed heat (kJ/mol)", zeroline: true };
  layout.xaxis2 = { domain: [0, 1], anchor: "y2", title: axisTitle };
  layout.yaxis2 = { domain: [0, .22], title: "Residual (kJ/mol)", zeroline: true };
  layout.height = 660;
  window.Plotly.newPlot(target, traces, layout, plotConfig);
  renderParameters(parameterBox, fit);

  function fitPointTrace(ids, name, color, symbol) {
    return { x: ids.map((i) => fit.x[i]), y: ids.map((i) => fit.observedKilojoulesPerMole[i]), error_y: { type: "data", array: ids.map((i) => fit.observationSdKilojoulesPerMole[i] || 0), visible: true, color }, name, type: "scatter", mode: "markers", marker: { color, symbol, size: 9 }, hovertemplate: "%{x:.5g}<br>%{y:.5g} kJ/mol<extra>" + name + "</extra>" };
  }
}

function renderParameters(target, fit) {
  target.hidden = false;
  const heading = document.createElement("h3");
  heading.textContent = "Fitted parameters";
  const table = document.createElement("table");
  const head = document.createElement("thead");
  const row = document.createElement("tr");
  ["Parameter", "Value", "SD", "95% interval"].forEach((text) => { const th = document.createElement("th"); th.textContent = text; row.append(th); });
  head.append(row);
  const body = document.createElement("tbody");
  fit.parameters.forEach((parameter) => {
    const tr = document.createElement("tr");
    const values = [parameter.label, formatNumber(parameter.value, parameter.unit ? ` ${parameter.unit}` : ""), formatNumber(parameter.sd, parameter.unit ? ` ${parameter.unit}` : ""), parameter.confidenceLower == null ? "—" : `${formatNumber(parameter.confidenceLower)} – ${formatNumber(parameter.confidenceUpper)}${parameter.unit ? ` ${parameter.unit}` : ""}`];
    values.forEach((value) => appendCell(tr, value));
    body.append(tr);
  });
  table.append(head, body);
  target.replaceChildren(heading, table);
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
function appendCell(row, value) { const cell = document.createElement("td"); cell.textContent = value == null ? "—" : String(value); row.append(cell); }
function appendParameterCell(row, parameter) {
  const cell = document.createElement("td");
  if (!parameter) { cell.textContent = "—"; row.append(cell); return; }
  const value = document.createElement("span");
  value.textContent = formatNumber(parameter.value, parameter.unit ? ` ${parameter.unit}` : "");
  cell.append(value);
  const details = [];
  if (Number.isFinite(Number(parameter.sd))) details.push(`SD ${formatNumber(parameter.sd)}`);
  if (parameter.confidenceLower != null) details.push(`95% ${formatNumber(parameter.confidenceLower)}–${formatNumber(parameter.confidenceUpper)}`);
  if (details.length) { const small = document.createElement("small"); small.textContent = details.join(" · "); cell.append(document.createElement("br"), small); }
  row.append(cell);
}
function indices(values, expected) { return values.map((value, index) => value === expected ? index : -1).filter((index) => index >= 0); }
function isComparableEnergy(parameter) { return parameter?.unit === "kJ/mol" && /enthalpy|gibbs|entropy/i.test(`${parameter.key} ${parameter.label}`); }
function validityLabel(status) { return ({ valid: "Valid saved result", partialInvalid: "Partially valid saved result", invalid: "Invalid saved result", unknown: "Validity not recorded" })[status] || "Validity not recorded"; }
function title(value) { return value.charAt(0).toUpperCase() + value.slice(1); }
function formatNumber(value, suffix = "") { return value == null || !Number.isFinite(Number(value)) ? "Unavailable" : `${numberFormatter.format(Number(value))}${suffix}`; }
function formatDate(value) {
  if (!value) return "Unavailable";
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return "Unavailable";
  const pad = (part) => String(part).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
function formatBytes(bytes) { if (bytes < 1024) return `${bytes} B`; if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`; return `${(bytes / 1024 / 1024).toFixed(1)} MB`; }
function baseLayout(xTitle, yTitle) { return { autosize: true, height: 540, margin: { l: 70, r: 65, t: 35, b: 60 }, paper_bgcolor: "#fff", plot_bgcolor: "#fff", hovermode: "closest", legend: { orientation: "h", y: 1.08 }, xaxis: { title: xTitle, gridcolor: "#edf1f1" }, yaxis: { title: yTitle, gridcolor: "#edf1f1", zerolinecolor: "#bdcaca" } }; }
