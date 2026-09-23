const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../AnalysisITC.Web/wwwroot/app.js'), 'utf8');
const start = source.indexOf('function evaluateTemperatureDependence(');
const end = source.indexOf('function addTemperatureEvaluationRow(', start);
assert.ok(start >= 0 && end > start);
const context = vm.createContext({});
vm.runInContext(source.slice(start, end), context);
const evaluate = context.evaluateTemperatureDependence;
const contribution = { weight: 300, weightSlope: 1, sd: 1, lowerWidth: 2, upperWidth: 3 };
const line = { referenceTemperatureCelsius: 26.85, intercept: -30000, slope: -100, lowerOffset: 0, upperOffset: 0, contributions: [contribution] };

test('single-source Gibbs uncertainty uses absolute temperature at every displayed temperature', () => {
  for (const t of [280, 300, 320]) {
    const value = evaluate(line, t - 273.15);
    assert.ok(Math.abs(value.value + 100 * t) < 1e-8);
    assert.ok(Math.abs(value.sd - t) < 1e-8);
    assert.ok(Math.abs(value.confidenceLower - (-102 * t)) < 1e-8);
    assert.ok(Math.abs(value.confidenceUpper - (-97 * t)) < 1e-8);
  }
});
test('legacy linear zero-kelvin reference remains finite without optional coefficients', () => {
  const value = evaluate({ ...line, referenceTemperatureCelsius: -273.15, intercept: 0, contributions: [{ ...contribution, weight: 0 }] }, 26.85);
  assert.equal(value.value, -30000);
  assert.equal(value.sd, 300);
});
test('zero-weight unavailable uncertainty is ignored', () => {
  const value = evaluate({ ...line, contributions: [{ ...contribution, weight: 0, weightSlope: 0, sd: null, lowerWidth: null, upperWidth: null }] }, 25);
  assert.equal(value.sd, 0);
  assert.equal(value.confidenceLower, value.value);
});
test('required nonlinear terms retain invalid-temperature handling', () => {
  const invalid = { ...line, referenceTemperatureCelsius: -273.15 };
  assert.ok(Number.isNaN(evaluate({ ...invalid, heatCapacityTerm: 1 }, 25).value));
  assert.equal(evaluate({ ...invalid, contributions: [{ ...contribution, weightHeatCapacityTerm: 1 }] }, 25).sd, null);
});
test('replicate evaluation retains joint coordinates and primary center', () => {
  const primary = { ...line, referenceTemperatureCelsius: 26.85, intercept: -25, slope: .05, contributions: [],
    replicates: [
      { ...line, referenceTemperatureCelsius: 26.85, intercept: -24.9, slope: .045, contributions: [] },
      { ...line, referenceTemperatureCelsius: 26.85, intercept: -25.1, slope: .055, contributions: [] }
    ] };
  const result = evaluate(primary, 46.85);
  assert.equal(result.value, -24);
  assert.ok(result.sd < 1e-12);
  assert.equal(result.confidenceLower, -24);
  assert.equal(result.confidenceUpper, -24);
});
test('nonlinear replicate curves use their own references and percentile endpoints', () => {
  const curves = [280, 300, 320].map(t => ({ ...line, referenceTemperatureCelsius: t - 273.15,
    intercept: -20, slope: .03, heatCapacityTerm: .65, contributions: [] }));
  for (const temperature of [6.85, 26.85, 46.85]) {
    const result = evaluate({ ...line, intercept: -25, contributions: [], replicates: curves }, temperature);
    const samples = curves.map(curve => {
      const t = temperature + 273.15, tr = curve.referenceTemperatureCelsius + 273.15;
      return -20 + .03 * (t - tr) + .65 * ((t - tr) - t * Math.log(t / tr));
    }).sort((a, b) => a - b);
    assert.ok(Math.abs(result.confidenceLower - samples[0]) < 1e-10);
    assert.ok(Math.abs(result.confidenceUpper - samples[2]) < 1e-10);
  }
});

test('bootstrap affinity spread is computed from transformed samples', () => {
  const curve = { ...line, referenceTemperatureCelsius: 26.85, intercept: -25, slope: 0, contributions: [],
    replicates: [-24, -27].map(intercept => ({ ...line, referenceTemperatureCelsius: 26.85, intercept, slope: 0, contributions: [] })) };
  const actual = context.deriveAffinity(evaluate(curve, 26.85), 26.85);
  const center = Math.exp(-25000 / (300 * 8.3145));
  const samples = [-24000, -27000].map(g => Math.exp(g / (300 * 8.3145)));
  const expectedSd = Math.sqrt(samples.reduce((sum, x) => sum + (x - center) ** 2, 0) / 2);
  assert.equal(actual.unit, 'µM');
  assert.ok(Math.abs(actual.value - center * 1e6) < 1e-9);
  assert.ok(Math.abs(actual.sd - expectedSd * 1e6) < 1e-9);
  assert.ok(Math.abs(actual.confidenceLower - samples[1] * 1e6) < 1e-9);
  assert.ok(Math.abs(actual.confidenceUpper - samples[0] * 1e6) < 1e-9);
});
