// The verso theme. `astryx experimental theme editor` wrote this file once and never changes it again.
// The checked settings live in verso.settings.json and the generated verso.settings.ts;
// change them with `astryx experimental theme editor set verso ...`. Add your own changes below: they win over the settings.
import {defineTheme} from '@astryxdesign/core/theme';
import {versoSettings} from './verso.settings';

export const versoTheme = defineTheme({
  extends: versoSettings,
  name: 'verso',
  localTokens: {
    '--verso-wave-green': ['#70b9ad', '#72c7ba'],
    '--verso-wave-purple': ['#9887d2', '#ad9ee2'],
  },
  tokens: {
    '--color-background-body': ['#fffdfa', '#242033'],
    '--color-background-surface': ['#fffdfa', '#2c273f'],
    '--color-background-card': ['#ffffff', '#2c273f'],
    '--color-text-primary': ['#27233c', '#f3effa'],
    '--color-text-secondary': ['#6b687b', '#c6c0d3'],
    '--color-border': ['#e8e5ee', '#514a69'],
    '--color-border-emphasized': ['#d8d2e6', '#71678a'],
    '--color-background-teal': ['#e4f4f0', '#253c3b'],
    '--color-border-teal': ['#70b9ad', '#72c7ba'],
    '--color-icon-teal': ['#168b83', '#72c7ba'],
    '--color-text-teal': ['#11766f', '#a7e2d8'],
    '--color-background-purple': ['#efebfb', '#39334e'],
    '--color-border-purple': ['#9887d2', '#ad9ee2'],
    '--color-icon-purple': ['#5a42a0', '#ad9ee2'],
    '--color-text-purple': ['#37266e', '#d4c9f0'],
  },
});
