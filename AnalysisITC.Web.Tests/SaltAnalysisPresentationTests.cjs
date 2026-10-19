const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../AnalysisITC.Web/wwwroot/app.js'), 'utf8');

function browser() {
  const elements = new Map();
  function element() { return { hidden: false, textContent: '', children: [], dataset: {},
    replaceChildren(...children) { this.children = children; },
    append(...children) { this.children.push(...children); },
    setAttribute() {}, addEventListener() {} }; }
  const context = vm.createContext({ document: { documentElement: { dataset: {} }, addEventListener() {},
    createElement: element, getElementById(id) { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); } },
    window: { Plotly: { purge() {} } } });
  vm.runInContext(source, context);
  vm.runInContext(`state.advancedAnalysisKind = 'electrostatics';
    renderAdvancedSummary = (metadata, parameters) => { globalThis.parameters = parameters; };
    renderAdvancedPlot = () => {};`, context);
  return { context, elements };
}

test('saved complete and partial electrostatics remain visible despite current ineligibility', () => {
  for (const counter of [null, { value: 0 }]) {
    const { context, elements } = browser();
    context.result = { advancedAnalyses: { electrostaticsUnavailableReason: 'Requires three spaced conditions.',
      electrostatics: { kd0Micromolar: { value: 2 }, counterIonRelease: counter,
        plots: [{ key: 'debye-huckel', title: 'Debye-Huckel', series: [{ x: [0, 1], y: [0, 1] }] }] } } };
    vm.runInContext('renderAdvancedAnalysis(result)', context);
    assert.equal(elements.get('result-advanced-card').hidden, false);
    assert.equal(elements.get('advanced-analysis-message').hidden, true);
    assert.equal(context.parameters.find(value => value.label === 'Kd0').value.value, 2);
    assert.equal(context.parameters.find(value => value.label === 'Counter-ion release').value, counter);
  }
});

test('missing electrostatics values render as unavailable while a zero remains numeric', () => {
  const { context, elements } = browser();
  vm.runInContext(source.slice(source.indexOf('function renderAdvancedSummary('), source.indexOf('function renderAdvancedPlot(')), context);
  vm.runInContext(`renderAdvancedSummary([], [advancedParameter('Kd0', null, 'µM'), advancedParameter('Counter-ion release', {value: 0}, '')])`, context);
  const table = elements.get('advanced-analysis-parameter-table').children[0];
  const rows = table.children[1].children;
  assert.equal(rows[0].children[1].textContent, 'Unavailable');
  assert.notEqual(rows[1].children[1].textContent, 'Unavailable');
  assert.match(rows[1].children[1].textContent, /0/);
});
