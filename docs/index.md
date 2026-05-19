---
layout: default
title: Pipeline to GitHub Actions Converter — AI-powered migration
description: Open-source .NET 10 tool that uses GitHub Copilot to convert GitLab CI, Azure DevOps, and Jenkins pipelines into production-ready GitHub Actions workflows.
---

<section class="hero">
  <div class="container">
    <div class="badge">⚡ Powered by the GitHub Copilot SDK · Open Source</div>
    <h1>
      Migrate any CI pipeline to<br>
      <span class="grad">GitHub Actions — with AI.</span>
    </h1>
    <p class="lede">
      Stop hand-translating YAML. <strong>ghcp-action-importer</strong> uses GitHub Copilot to analyze, convert, and validate your
      GitLab CI, Azure DevOps, and Jenkins pipelines into production-ready GitHub Actions workflows — in parallel, at scale.
    </p>
    <div class="hero-ctas">
      <a class="btn btn-primary" href="#quickstart">🚀 Get started in 60 seconds</a>
      <a class="btn btn-secondary" href="https://github.com/Frank802/ghcp-action-importer">⭐ Star on GitHub</a>
    </div>
    <div class="hero-stats">
      <div class="hero-stat"><div class="num">3</div><div class="lbl">CI platforms</div></div>
      <div class="hero-stat"><div class="num">3</div><div class="lbl">AI phases per pipeline</div></div>
      <div class="hero-stat"><div class="num">N×</div><div class="lbl">Parallel sessions</div></div>
      <div class="hero-stat"><div class="num">100%</div><div class="lbl">Open source</div></div>
    </div>

    <div class="logos-strip">
      <div class="label">Converts pipelines from</div>
      <div class="logos">
        <span>🦊 GitLab CI</span>
        <span>🔷 Azure DevOps</span>
        <span>🤵 Jenkins</span>
        <span class="arrow">→</span>
        <span>🐙 GitHub Actions</span>
      </div>
    </div>
  </div>
</section>

<section id="features">
  <div class="container">
    <div class="section-head">
      <span class="eyebrow">Features</span>
      <h2>Built to handle real-world pipelines</h2>
      <p>Not just a YAML transformer — a three-phase AI workflow that reasons about your pipeline, flags risks, converts intelligently, and validates the result.</p>
    </div>
    <div class="feature-grid">
      <div class="feature-card">
        <div class="icon">🔬</div>
        <h3>Pre-conversion analysis</h3>
        <p>Copilot evaluates complexity (Low/Medium/High/Critical), identifies risks and unsupported features, and can block conversion on critical issues.</p>
      </div>
      <div class="feature-card">
        <div class="icon">🤖</div>
        <h3>AI-powered conversion</h3>
        <p>Analysis findings are injected into the converter prompt, so the model adds TODOs, handles edge cases, and produces idiomatic Actions YAML.</p>
      </div>
      <div class="feature-card">
        <div class="icon">✅</div>
        <h3>Validation & auto-fix</h3>
        <p>YAML syntax, Actions structure, security best practices, and action version pinning are checked. Improvements are applied directly to the output.</p>
      </div>
      <div class="feature-card">
        <div class="icon">⚡</div>
        <h3>Parallel sessions</h3>
        <p>Process dozens of pipelines concurrently using independent Copilot sessions throttled by a configurable semaphore.</p>
      </div>
      <div class="feature-card">
        <div class="icon">📊</div>
        <h3>Live Blazor dashboard</h3>
        <p>Pass <code>--port</code> to launch a real-time progress UI: per-pipeline phase indicators, elapsed time, and error details.</p>
      </div>
      <div class="feature-card">
        <div class="icon">📝</div>
        <h3>Customizable prompts</h3>
        <p>Each phase is driven by a plain markdown prompt file. Tweak instructions for your stack without touching code.</p>
      </div>
      <div class="feature-card">
        <div class="icon">🧩</div>
        <h3>Extensible by design</h3>
        <p>Add new pipeline sources by implementing <code>IPipelineSource</code>. CircleCI, Bitbucket, TeamCity — bring your own.</p>
      </div>
      <div class="feature-card">
        <div class="icon">📑</div>
        <h3>Audit-ready reports</h3>
        <p>Every converted workflow ships with <code>.analysis.md</code> and <code>.validation.md</code> reports for review, sign-off, and compliance.</p>
      </div>
    </div>
  </div>
</section>

<section id="how-it-works" class="how-section">
  <div class="container">
    <div class="section-head">
      <span class="eyebrow">How it works</span>
      <h2>Five steps. One Copilot session per pipeline.</h2>
      <p>Analysis, conversion, and validation all run in the <em>same</em> Copilot session — preserving context for higher-quality output.</p>
    </div>

    <div class="diagram-card">
      <div class="mermaid">
flowchart LR
    Input["📁 Input<br/>.gitlab-ci.yml<br/>azure-pipelines.yml<br/>Jenkinsfile"] --> Scan["🔍 Scanner"]
    Scan --> Pool{{"⚙️ Parallel Pool<br/>N Copilot sessions"}}
    Pool --> S1["🔬 Analyze<br/>→ 🤖 Convert<br/>→ ✅ Validate"]
    S1 --> Write["💾 Writer"]
    Write --> Output["📂 .github/workflows/<br/>workflow.yml<br/>analysis.md<br/>validation.md"]
    Pool -.-> Dash["📊 Live dashboard"]
      </div>
    </div>

    <div class="steps">
      <div class="step"><div class="num">1</div><h4>Scan</h4><p>Discover pipeline files by matching against registered <code>IPipelineSource</code> patterns.</p></div>
      <div class="step"><div class="num">2</div><h4>Analyze</h4><p>Copilot rates complexity, surfaces risks, and flags unsupported features. Critical issues can block conversion.</p></div>
      <div class="step"><div class="num">3</div><h4>Convert</h4><p>In the same session, the converter produces idiomatic Actions YAML informed by the analysis.</p></div>
      <div class="step"><div class="num">4</div><h4>Validate</h4><p>Syntax, security, and pinning are checked. Improvements are auto-applied to the output workflow.</p></div>
      <div class="step"><div class="num">5</div><h4>Report</h4><p>Save the workflow plus analysis &amp; validation reports — and watch progress live via the Blazor dashboard.</p></div>
    </div>
  </div>
</section>

<section id="demo">
  <div class="container">
    <div class="section-head">
      <span class="eyebrow">Before · After</span>
      <h2>From GitLab YAML to Actions, instantly</h2>
      <p>Real input from the bundled sample, real Copilot output. Including <code>checkout</code>, <code>setup-node</code> with caching, and pinned action versions.</p>
    </div>

    <div class="demo-grid">
      <div class="code-panel">
        <div class="head">
          <div class="dots"><span class="dot red"></span><span class="dot yellow"></span><span class="dot green"></span></div>
          <span class="name">.gitlab-ci.yml</span>
          <span></span>
        </div>
<pre><code>stages:
  - build
  - test
  - deploy

build:
  stage: build
  image: node:20
  script:
    - npm ci
    - npm run build
  artifacts:
    paths:
      - dist/

test:
  stage: test
  script:
    - npm test</code></pre>
      </div>

      <div class="arrow-big">→</div>

      <div class="code-panel">
        <div class="head">
          <div class="dots"><span class="dot red"></span><span class="dot yellow"></span><span class="dot green"></span></div>
          <span class="name">.github/workflows/ci.yml</span>
          <span></span>
        </div>
<pre><code>name: CI/CD Pipeline

on:
  push:
    branches: [main, develop]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
          cache: 'npm'
      - run: npm ci
      - run: npm run build
      - uses: actions/upload-artifact@v4
        with:
          name: dist
          path: dist/

  test:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: npm test</code></pre>
      </div>
    </div>
  </div>
</section>

<section id="quickstart">
  <div class="container">
    <div class="section-head">
      <span class="eyebrow">Quick start</span>
      <h2>Up and running in under a minute</h2>
      <p>Clone, build, run. The included samples cover GitLab CI, Azure DevOps, and Jenkins out of the box.</p>
    </div>

    <div class="quickstart-grid">
      <div class="code-panel">
        <div class="head">
          <div class="dots"><span class="dot red"></span><span class="dot yellow"></span><span class="dot green"></span></div>
          <span class="name">terminal</span>
          <span></span>
        </div>
<pre><code># 1. Clone
git clone https://github.com/Frank802/ghcp-action-importer.git
cd ghcp-action-importer/src

# 2. Build
dotnet build

# 3. Convert the bundled samples
dotnet run -- -i ../samples -o ../output -v

# 4. (Optional) Launch the live dashboard
dotnet run -- -i ../samples -o ../output -p 5050</code></pre>
      </div>

      <div class="requirements">
        <h3>What you'll need</h3>
        <ul>
          <li><a href="https://dotnet.microsoft.com/download">.NET 10 SDK</a></li>
          <li><a href="https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli">GitHub Copilot CLI</a> (authenticated)</li>
          <li>Active GitHub Copilot subscription</li>
          <li>Pipelines you want to migrate 🚀</li>
        </ul>
      </div>
    </div>
  </div>
</section>

<section id="sources">
  <div class="container">
    <div class="section-head">
      <span class="eyebrow">Supported sources</span>
      <h2>Bring your pipelines — we'll handle the rest</h2>
      <p>Three sources supported today, with a clean extension point for more.</p>
    </div>
    <div class="sources-grid">
      <div class="source-card">
        <div class="logo">🦊</div>
        <h4>GitLab CI/CD</h4>
        <p style="color:var(--text-muted);margin:6px 0 10px;">Stages, jobs, scripts, artifacts, includes</p>
        <code>.gitlab-ci.yml</code>
      </div>
      <div class="source-card">
        <div class="logo">🔷</div>
        <h4>Azure DevOps</h4>
        <p style="color:var(--text-muted);margin:6px 0 10px;">Stages, jobs, steps, templates, variables</p>
        <code>azure-pipelines.yml</code>
      </div>
      <div class="source-card">
        <div class="logo">🤵</div>
        <h4>Jenkins</h4>
        <p style="color:var(--text-muted);margin:6px 0 10px;">Declarative &amp; scripted pipelines, agents, stages</p>
        <code>Jenkinsfile</code>
      </div>
    </div>
  </div>
</section>

<section id="cli">
  <div class="container">
    <div class="section-head">
      <span class="eyebrow">CLI reference</span>
      <h2>Powerful flags, sensible defaults</h2>
    </div>

<table style="width:100%;border-collapse:collapse;background:var(--bg-elev);border:1px solid var(--border);border-radius:12px;overflow:hidden;">
<thead style="background:var(--bg-elev-2);">
<tr>
<th style="text-align:left;padding:14px 16px;border-bottom:1px solid var(--border);">Option</th>
<th style="text-align:left;padding:14px 16px;border-bottom:1px solid var(--border);">Alias</th>
<th style="text-align:left;padding:14px 16px;border-bottom:1px solid var(--border);">Description</th>
</tr>
</thead>
<tbody>
<tr><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>--input</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>-i</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><strong>Required.</strong> Input directory containing pipeline files</td></tr>
<tr><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>--output</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>-o</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><strong>Required.</strong> Output directory for converted workflows</td></tr>
<tr><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>--source</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>-s</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);">Filter: <code>GitLab</code>, <code>AzureDevOps</code>, <code>Jenkins</code></td></tr>
<tr><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>--max-sessions</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>-m</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);">Max parallel Copilot sessions (default: 3)</td></tr>
<tr><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>--port</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>-p</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);">Start the Blazor dashboard on the given port</td></tr>
<tr><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>--skip-validation</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);">Skip validation step</td></tr>
<tr><td style="padding:12px 16px;border-bottom:1px solid var(--border);"><code>--skip-analysis</code></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);"></td><td style="padding:12px 16px;border-bottom:1px solid var(--border);">Skip pre-conversion analysis</td></tr>
<tr><td style="padding:12px 16px;"><code>--verbose</code></td><td style="padding:12px 16px;"><code>-v</code></td><td style="padding:12px 16px;">Enable verbose output</td></tr>
</tbody>
</table>
  </div>
</section>

<section>
  <div class="container">
    <div class="cta-banner">
      <h2>Ready to free your team from YAML drudgery?</h2>
      <p>Star the repo, try it on your hardest pipeline, and tell us what breaks. Issues and PRs welcome.</p>
      <div class="hero-ctas">
        <a class="btn btn-primary" href="https://github.com/Frank802/ghcp-action-importer">⭐ Star on GitHub</a>
        <a class="btn btn-secondary" href="https://github.com/Frank802/ghcp-action-importer/issues/new">💬 Open an issue</a>
        <a class="btn btn-secondary" href="https://github.com/Frank802/ghcp-action-importer#readme">📖 Read the docs</a>
      </div>
    </div>
  </div>
</section>
