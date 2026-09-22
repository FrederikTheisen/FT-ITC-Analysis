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
  browser.renderTemperatureParameterEvaluation({ temperatureParameterEvaluation: {
    defaultTemperatureCelsius: 20, minimumTemperatureCelsius: 20, maximumTemperatureCelsius: 20, isTemperatureDependent: false,
    dependences: [{ ...summary([contribution(1, 0, 1, 2, 4)]), family: 'Enthalpy', slotIndex: 1, heatCapacity: { value: 0 } }]
  } });
  const table = elements.get('result-temperature-evaluation-table').children[0];
  const header = table.children[0].children[0].children.map(cell => cell.textContent);
  assert.deepEqual(header, ['Parameter', 'Value', 'SD', 'Interval']);
  assert.match(elements.get('temperature-evaluation-note').textContent, /Approximate propagated interval/);
  assert.match(elements.get('temperature-evaluation-note').textContent, /Model-estimated intervals retain their CI95 meaning/);
});
