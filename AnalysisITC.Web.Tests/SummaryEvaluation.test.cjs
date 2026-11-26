// Run with: node --test AnalysisITC.Web.Tests/SummaryEvaluation.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const vm = require('node:vm');
const browser = vm.createContext({ document: { documentElement: { dataset: {} }, addEventListener() {} } });
vm.runInContext(readFileSync(join(__dirname, '../AnalysisITC.Web/wwwroot/app.js'), 'utf8'), browser);
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-12, `${actual} != ${expected}`);
const contribution = (weight, weightSlope, sd, lowerWidth, upperWidth = lowerWidth) =>
  ({ weight, weightSlope, sd, lowerWidth, upperWidth });
const summary = (contributions, intercept = 11, slope = 0, referenceTemperatureCelsius = 20) =>
  ({ contributions, intercept, slope, referenceTemperatureCelsius, lowerOffset: 0, upperOffset: 0 });

test('browser evaluates asymmetric mean including observed spread', () => {
  const input = summary([
    contribution(1, 0, Math.sqrt(7), 1.96),
    contribution(.5, 0, 0, 2, 4), contribution(.5, 0, 0, 1, 3)
  ]);
  const actual = browser.evaluateTemperatureDependence(input, 20);
  near(actual.value, 11);
  near(actual.sd, Math.sqrt(7));
  near(actual.confidenceLower, 11 - Math.sqrt(5.0916));
  near(actual.confidenceUpper, 11 + Math.sqrt(10.0916));
});

test('browser swaps interval sides at negative regression weights', () => {
  const input = summary([
    contribution(1, 0, Math.sqrt(5), 0),
    contribution(.5, -.1, 0, 2, 4), contribution(.5, .1, 0, 1, 3)
  ], 15, 1, 25);
  const actual = browser.evaluateTemperatureDependence(input, 40);
  near(actual.value, 30);
  near(actual.sd, Math.sqrt(5));
  near(actual.confidenceLower, 30 - Math.sqrt(20));
  near(actual.confidenceUpper, 30 + Math.sqrt(40));
});

test('browser includes reference and slope residual contributions', () => {
  const input = summary([
    contribution(1, 0, Math.sqrt(3), 1.96 * Math.sqrt(.5)),
    contribution(0, 1, 0, 1.96 * Math.sqrt(.02)),
    contribution(.25, -.05, 0, 1.96), contribution(.25, -.05, 0, 1.96),
    contribution(.25, .05, 0, 1.96), contribution(.25, .05, 0, 1.96)
  ], 6, 1, 25);
  const actual = browser.evaluateTemperatureDependence(input, 35);
  near(actual.value, 16);
  near(actual.sd, Math.sqrt(3));
  near(actual.confidenceLower, 16 - 1.96 * Math.sqrt(3.75));
  near(actual.confidenceUpper, 16 + 1.96 * Math.sqrt(3.75));
});

test('bootstrap percentile ranks agree with desktop at and around multiples of 40', () => {
  for (const [count, lower, upper] of [[39, 1, 39], [40, 1, 40], [41, 2, 40], [200, 5, 196], [1000, 25, 976]]) {
    const fit = summary([], 10);
    fit.replicates = Array.from({ length: count }, (_, i) => summary([], count - i));
    const result = browser.evaluateTemperatureDependence(fit, 20);
    assert.deepEqual([result.confidenceLower, result.confidenceUpper], [lower, upper]);
    assert.equal(result.value, 10);
  }
  const single = browser.evaluateTemperatureDependence({ ...summary([], 10), replicates: [summary([], 12)] }, 20);
  assert.deepEqual([single.sd, single.confidenceLower, single.confidenceUpper], [0, 10, 10]);
});

test('absent uncertainties are blank and a valid interval can be shown independently of SD', () => {
  for (const sd of [0, null, NaN]) assert.equal(browser.formatParameterUncertainty(sd, 'J/mol'), '');
  const value = { value: 10, sd: 0, confidenceLower: 10, confidenceUpper: 10 };
  assert.equal(browser.formatParameterInterval(value), '');
  assert.equal(browser.formatParameterInterval({ ...value, confidenceLower: null, confidenceUpper: null }), '');
  assert.equal(browser.formatParameterInterval({ ...value, confidenceLower: 9, confidenceUpper: 11 }, 'J/mol'), '9 – 11 J/mol');
});

test('single non-bracketing interval is retained directly', () => {
  const input = summary([contribution(1, 0, 2, 0)], 10);
  input.lowerOffset = 1;
  input.upperOffset = 4;
  const actual = browser.evaluateTemperatureDependence(input, 20);
  near(actual.confidenceLower, 11);
  near(actual.confidenceUpper, 14);
});

test('unavailable interval and SD never become zero through null coercion', () => {
  const actual = browser.evaluateTemperatureDependence(summary([contribution(1, 0, null, null, null)]), 20);
  assert.equal(actual.sd, null);
  assert.equal(actual.confidenceLower, null);
  assert.equal(actual.confidenceUpper, null);
  const kd = browser.deriveAffinity(actual, 20);
  assert.equal(kd.sd, null);
  assert.equal(kd.confidenceLower, null);
  assert.equal(kd.confidenceUpper, null);
  assert.equal(browser.formatParameterNumber(actual.sd, actual.sd), 'Unavailable');
});

test('Kd transforms both asymmetric Gibbs endpoints without changing central convention', () => {
  const gibbs = { value: -30, sd: 1, confidenceLower: -32, confidenceUpper: -25 };
  const kd = browser.deriveAffinity(gibbs, 25);
  const factor = 1000 / (8.3145 * 298.15);
  const scale = browser.concentrationDisplayScale(Math.exp(-30 * factor)).scale;
  near(kd.value, Math.exp(-30 * factor) * scale);
  near(kd.confidenceLower, Math.exp(-32 * factor) * scale);
  near(kd.confidenceUpper, Math.exp(-25 * factor) * scale);
  assert.ok(kd.confidenceUpper - kd.value > kd.value - kd.confidenceLower);
});

test('temperature summary labels the interval without relabeling individual uncertainty', () => {
  const element = () => ({ children: [], append(...children) { this.children.push(...children); }, replaceChildren(...children) { this.children = children; } });
  const elements = new Map();
  browser.document.createElement = element;
  browser.document.getElementById = (id) => {
    if (!elements.has(id)) elements.set(id, element());
    return elements.get(id);
  };
  const render = () => browser.renderTemperatureParameterEvaluation({ temperatureParameterEvaluation: {
    defaultTemperatureCelsius: 20, minimumTemperatureCelsius: 20, maximumTemperatureCelsius: 20, isTemperatureDependent: false,
    summaryUncertaintyToolTip: "Mean explanation", replicateSpreadToolTip: "Spread explanation",
    dependences: [{
      ...summary([contribution(1, 0, 1, 2, 4)]), family: 'Enthalpy', slotIndex: 1, heatCapacity: { value: 0 },
      statisticLabel: 'SEM', intervalLabel: 'Approximate 95% interval for mean',
      replicateSpread: { ...summary([contribution(1, 0, 3, 5)]), statisticLabel: 'Replicate SD', intervalLabel: 'Approximate 95% replicate range' }
    }, {
      ...summary([contribution(1, 0, 1, 2, 4)]), family: 'EntropyContribution', slotIndex: 1,
      statisticLabel: 'SD', intervalLabel: '95% confidence interval'
    }]
  } });
  render();
  const table = elements.get('result-temperature-evaluation-table').children[0];
  const header = table.children[0].children[0].children.map(cell => cell.textContent);
  assert.deepEqual(header, ['Parameter', 'Value', 'Uncertainty', 'Interval']);
  const [mean, model] = table.children[1].children.map(row => row.children);
  assert.equal(mean[2].textContent, "1.0 kJ/mol");
  assert.equal(mean[2].title, "SEM");
  assert.deepEqual(elements.get('summary-uncertainty-select').children.map(item => item.textContent), ["SEM", "Replicate SD"]);
  assert.equal(mean[3].title, "Approximate 95% interval for mean");
  assert.equal(model[3].title, "95% confidence interval");
  assert.equal(elements.get('summary-uncertainty-control').hidden, false);
  assert.equal(elements.get('summary-uncertainty-select').title, 'Mean explanation');

  vm.runInContext('state.summaryUncertaintyMode = "replicateSpread";', browser);
  try {
    render();
    const [spread, other] = elements.get('result-temperature-evaluation-table').children[0].children[1].children
      .map(row => row.children);
    assert.equal(spread[1].textContent, mean[1].textContent);
    assert.equal(spread[2].textContent, "3.0 kJ/mol");
    assert.equal(spread[2].title, "Replicate SD");
    assert.equal(spread[3].title, "Approximate 95% replicate range");
    assert.equal(other[3].title, model[3].title);
    assert.equal(elements.get('summary-uncertainty-select').title, 'Spread explanation');
  } finally {
    vm.runInContext('state.summaryUncertaintyMode = "mean";', browser);
  }
});

test('temperature renderer preserves a fractional default evaluation temperature', () => {
  const element = () => ({ children: [], value: '', append(...children) { this.children.push(...children); }, replaceChildren(...children) { this.children = children; } });
  const elements = new Map();
  browser.document.createElement = element;
  browser.document.getElementById = (id) => {
    if (!elements.has(id)) elements.set(id, element());
    return elements.get(id);
  };
  vm.runInContext('state.resultEvaluationTemperature = null;', browser);
  browser.renderTemperatureParameterEvaluation({ temperatureParameterEvaluation: {
    defaultTemperatureCelsius: 23.37, minimumTemperatureCelsius: 23.37, maximumTemperatureCelsius: 23.37, isTemperatureDependent: false,
    dependences: [{ ...summary([contribution(1, 0, 0, 0)]), family: 'Enthalpy', slotIndex: 1, heatCapacity: { value: 0 } }]
  } });
  assert.equal(elements.get('result-evaluation-temperature').value, '23.37');
  const cells = elements.get('result-temperature-evaluation-table').children[0].children[1].children[0].children;
  assert.equal(cells[1].textContent, '11 kJ/mol');
  assert.equal(cells[2].textContent, '');
  assert.equal(cells[3].textContent, '');
});

test('temperature plot switches local bands and preserves individual error bars', () => {
  let traces;
  browser.window = { Plotly: { newPlot(_target, data) { traces = data; } } };
  const series = { group: 'enthalpy', x: [20, 21, 22, 23, 24, 25, 26], y: [11, 12, null, 14, 15, 16, 17] };
  const plot = { key: 'temperature-dependence', series: [
    { group: 'enthalpy', kind: 'points', x: [20, 21], y: [11, 12], lower: [13, null], upper: [14, 15] },
    {
      ...series, kind: 'line', lower: [13, 14, 11, 13, 14, null, 16], upper: [14, 15, 12, 15, 16, null, 18],
      intervalLabel: 'Approximate 95% interval for fitted trend',
      replicateSpread: { ...series, kind: 'line', lower: [8, 9, null, null, null, null, null],
        upper: [14, 15, null, null, null, null, null], intervalLabel: 'Approximate 95% replicate range' }
    }
  ] };
  browser.renderAdvancedPlot({}, plot);
  const [errorBars, points, firstBand, secondBand, center] = traces;
  assert.deepEqual(Array.from(errorBars.x), [20, 20, null]);
  assert.deepEqual(Array.from(errorBars.y), [13, 14, null]);
  assert.deepEqual(points.y, [11, 12]);
  assert.deepEqual(firstBand.x, [20, 21, 21, 20]);
  assert.deepEqual(firstBand.y, [13, 14, 15, 14]);
  assert.deepEqual(secondBand.x, [23, 24, 24, 23]);
  assert.deepEqual(center.y, series.y);
  assert.match(center.hovertemplate, /Approximate 95% interval for fitted trend/);
  vm.runInContext('state.summaryUncertaintyMode = "replicateSpread";', browser);
  try {
    browser.renderAdvancedPlot({}, plot);
    assert.deepEqual(traces[0], errorBars);
    assert.deepEqual(traces[1], points);
    assert.deepEqual(traces[2].y, [8, 9, 15, 14]);
    assert.deepEqual(traces[3].y, series.y);
    assert.match(traces[3].hovertemplate, /Approximate 95% replicate range/);
  } finally {
    vm.runInContext('state.summaryUncertaintyMode = "mean";', browser);
  }
});

test('only the temperature plot displays a caption and switches its interval text', () => {
  const element = () => ({
    children: [], dataset: {}, append(...children) { this.children.push(...children); },
    replaceChildren(...children) { this.children = children; }, setAttribute() {}, addEventListener() {}
  });
  const elements = new Map();
  browser.document.createElement = element;
  browser.document.getElementById = (id) => {
    if (!elements.has(id)) elements.set(id, element());
    return elements.get(id);
  };
  const series = [{ kind: 'points', x: [20], y: [11] }];
  const result = { advancedAnalyses: { spolarRecord: { temperatureDependencePlot: {
    key: 'temperature-dependence', series, intervalCaption: 'Mean caption', replicateSpreadIntervalCaption: 'Spread caption'
  } } } };
  browser.renderAdvancedAnalysis(result);
  const caption = elements.get('temperature-dependence-caption');
  assert.equal(caption.hidden, false);
  assert.equal(caption.textContent, 'Mean caption');
  vm.runInContext('state.summaryUncertaintyMode = "replicateSpread";', browser);
  try {
    browser.renderAdvancedAnalysis(result);
    assert.equal(caption.textContent, 'Spread caption');
    browser.renderAdvancedAnalysis({ advancedAnalyses: { protonation: { plot: { key: 'protonation', series } } } });
    assert.equal(caption.hidden, true);
  } finally {
    vm.runInContext('state.summaryUncertaintyMode = "mean";', browser);
  }
});

test('temperature renderer sends user-entered temperature to the evaluator', () => {
  const element = () => ({ children: [], value: '', append(...children) { this.children.push(...children); }, replaceChildren(...children) { this.children = children; } });
  const elements = new Map();
  browser.document.createElement = element;
  browser.document.getElementById = (id) => {
    if (!elements.has(id)) elements.set(id, element());
    return elements.get(id);
  };
  vm.runInContext('state.resultEvaluationTemperature = 40.25;', browser);
  browser.renderTemperatureParameterEvaluation({ temperatureParameterEvaluation: {
    defaultTemperatureCelsius: 23.37, minimumTemperatureCelsius: 10, maximumTemperatureCelsius: 50, isTemperatureDependent: false,
    dependences: [{ ...summary([contribution(1, 0, 0, 0)], 12, 1), family: 'Enthalpy', slotIndex: 1, heatCapacity: { value: 0 } }]
  } });
  assert.equal(elements.get('result-evaluation-temperature').value, '40.25');
  const row = elements.get('result-temperature-evaluation-table').children[0].children[1].children[0];
  assert.equal(row.children[1].textContent, '32.25 kJ/mol');
});
