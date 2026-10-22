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
  vm.runInContext("state.advancedAnalysisKind = 'protonation'; renderAdvancedPlot = () => {};", context);
  return { context, elements };
}

for (const [value, lower, upper] of [[0.5, 0.2, 0.9], [-0.5, -0.9, -0.2], [0, -0.2, 0.4]]) {
  test(`viewer renders saved signed uptake ${value} and its full asymmetric interval`, () => {
    const { context, elements } = browser();
    context.result = { advancedAnalyses: { protonation: {
      protonationChange: { value, sd: 0.1, confidenceLower: lower, confidenceUpper: upper },
      bindingEnthalpyKilojoulesPerMole: { value: -40, sd: 0.5, confidenceLower: -41.2, confidenceUpper: -39.4 }
    } } };
    vm.runInContext('renderAdvancedAnalysis(result)', context);
    const rows = elements.get('advanced-analysis-parameter-table').children[0].children[1].children;
    assert.equal(rows[1].children[0].textContent, 'Net proton uptake');
    assert.equal(Number(rows[1].children[1].textContent), value);
    assert.equal(Number(rows[1].children[2].textContent), 0.1);
    assert.ok(rows[1].children[3].textContent.includes(String(lower)));
    assert.ok(rows[1].children[3].textContent.includes(String(upper)));
    assert.equal(rows[0].children[0].textContent, 'Buffer-independent enthalpy');
    assert.match(rows[0].children[3].textContent, /-41.2.*-39.4/);
    const metadata = elements.get('advanced-analysis-metadata').children;
    assert.ok(metadata.some(row => row.children.some(cell => cell.textContent === 'Positive: uptake; negative: release.')));
  });
}
