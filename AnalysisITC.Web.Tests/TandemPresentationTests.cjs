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
    window: { Plotly: { purge() {}, newPlot() {} } } });
  vm.runInContext(source, context);
  vm.runInContext("renderParameters = () => {};", context);
  return { context, elements };
}


test('tandem details render saved limits, tooltips and literal source text', () => {
  const { context, elements } = browser();
  context.details = { settings: [{ label: 'Method', value: 'Automatic (model-free)' }],
    sourceRuns: '<b>source</b> & ' + 'long name '.repeat(30),
    refills: [{ fraction: '0.17', limits: '0 (bound reached) – 0.38' },
      { fraction: '0.4', limits: 'Unavailable' }], limitsTooltip: 'Monte Carlo 95% limits' };
  vm.runInContext('renderTandemDetails(document.getElementById("tandem-details"), details)', context);
  const target = elements.get('tandem-details');
  assert.equal(target.hidden, false);
  assert.equal(target.children[1].children[1].children[1].textContent, context.details.sourceRuns);
  const table = target.children[2];
  assert.equal(table.children[0].children[0].children[2].title, 'Monte Carlo 95% limits');
  assert.equal(table.children[1].children.length, 2);
  assert.equal(table.children[1].children[0].children[2].textContent, '0 (bound reached) – 0.38');
});

test('manual merges omit missing limits; ordinary experiments clear tandem details', () => {
  const { context, elements } = browser();
  context.details = { settings: [], refills: [{ fraction: '0.2', limits: null }], description: 'Recorded merge' };
  vm.runInContext('renderTandemDetails(document.getElementById("tandem-details"), details)', context);
  const target = elements.get('tandem-details');
  assert.equal(target.children[2].children[0].children[0].children.length, 2);
  assert.equal(target.children[3].textContent, 'Recorded merge');
  vm.runInContext('renderTandemDetails(document.getElementById("tandem-details"), null)', context);
  assert.equal(target.hidden, true);
  assert.equal(target.children.length, 0);
});

for (const status of ['Included in bootstrap', 'Fixed fractions', 'Unavailable', null]) {
  test('shared fit summary displays mixing treatment: ' + status, () => {
    const { context, elements } = browser();
    context.fit = { x: [], included: [], fittedKilojoulesPerMole: [], tandemMixingStatus: status };
    vm.runInContext('renderFitData({}, {}, fit, document.getElementById("summary"))', context);
    const rows = elements.get('summary').children;
    const mixing = rows.find(row => row.children[0].textContent === 'Tandem mixing uncertainty');
    assert.equal(mixing?.children[1].textContent, status || undefined);
  });
}
