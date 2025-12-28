import React, { useEffect, useMemo, useRef, useState } from "react";
import { defaultParameters, GalaxyParameters } from "@domain/parameters";
import { findPreset, presets } from "@domain/presets";
import { GalaxyRenderer } from "@gl/renderer";
import "./styles.css";

type ThemeName = "Nebula" | "Monochrome";

const themes: Record<
  ThemeName,
  {
    name: ThemeName;
    vars: Record<string, string>;
  }
> = {
  Nebula: {
    name: "Nebula",
    vars: {
      "--bg": "#0a0f1b",
      "--panel": "#111827",
      "--panel-border": "#1f2937",
      "--accent": "#ff8c5a",
      "--accent-2": "#5eead4",
      "--text": "#e5e7eb",
      "--muted": "#9ca3af",
      "--input": "#0f172a",
      "--input-border": "#1f2937",
      "--glow": "rgba(255, 140, 90, 0.24)"
    }
  },
  Monochrome: {
    name: "Monochrome",
    vars: {
      "--bg": "#111111",
      "--panel": "#1a1a1a",
      "--panel-border": "#262626",
      "--accent": "#5ea0ff",
      "--accent-2": "#9ae6b4",
      "--text": "#f3f4f6",
      "--muted": "#9ca3af",
      "--input": "#0f0f0f",
      "--input-border": "#262626",
      "--glow": "rgba(94, 160, 255, 0.2)"
    }
  }
};

type WorkerResult =
  | { type: "result"; id: number; count: number; buffer: ArrayBuffer }
  | { type: "error"; id: number; message: string };

const scrubMultiplier = (event: PointerEvent | React.PointerEvent) => {
  if (event.shiftKey) return 10;
  if (event.altKey) return 0.1;
  return 1;
};

export default function App() {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const rendererRef = useRef<GalaxyRenderer | null>(null);
  const workerRef = useRef<Worker | null>(null);
  const currentRequestId = useRef(0);
  const [params, setParams] = useState<GalaxyParameters>({ ...defaultParameters });
  const [presetName, setPresetName] = useState<string>("Default");
  const [theme, setTheme] = useState<ThemeName>("Nebula");
  const [status, setStatus] = useState("Ready");
  const [generating, setGenerating] = useState(false);
  const [rendererReady, setRendererReady] = useState(false);

  // Apply theme tokens to CSS variables
  useEffect(() => {
    const vars = themes[theme].vars;
    Object.entries(vars).forEach(([key, value]) => {
      document.documentElement.style.setProperty(key, value);
    });
  }, [theme]);

  // Init renderer
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const renderer = new GalaxyRenderer(canvas);
    rendererRef.current = renderer;
    try {
      renderer.init();
      renderer.resize();
      setRendererReady(true);
    } catch (error) {
      console.error(error);
      setStatus("Failed to init WebGL");
    }
    const onResize = () => {
      renderer.resize();
      renderer.render();
    };
    window.addEventListener("resize", onResize);
    return () => {
      window.removeEventListener("resize", onResize);
      renderer.dispose();
    };
  }, []);

  // Canvas interactions (orbit + zoom)
  useEffect(() => {
    if (!rendererReady) return;
    const canvas = canvasRef.current;
    const renderer = rendererRef.current;
    if (!canvas || !renderer) return;
    let dragging = false;
    let lastX = 0;
    let lastY = 0;
    const onDown = (e: PointerEvent) => {
      dragging = true;
      lastX = e.clientX;
      lastY = e.clientY;
      canvas.setPointerCapture(e.pointerId);
    };
    const onMove = (e: PointerEvent) => {
      if (!dragging) return;
      const dx = e.clientX - lastX;
      const dy = e.clientY - lastY;
      renderer.orbit(dx * 0.005, -dy * 0.005);
      lastX = e.clientX;
      lastY = e.clientY;
    };
    const onUp = (e: PointerEvent) => {
      dragging = false;
      canvas.releasePointerCapture(e.pointerId);
    };
    const onWheel = (e: WheelEvent) => {
      e.preventDefault();
      renderer.zoom(-e.deltaY * 0.05);
    };
    canvas.addEventListener("pointerdown", onDown);
    canvas.addEventListener("pointermove", onMove);
    canvas.addEventListener("pointerup", onUp);
    canvas.addEventListener("pointercancel", onUp);
    canvas.addEventListener("wheel", onWheel, { passive: false });
    return () => {
      canvas.removeEventListener("pointerdown", onDown);
      canvas.removeEventListener("pointermove", onMove);
      canvas.removeEventListener("pointerup", onUp);
      canvas.removeEventListener("pointercancel", onUp);
      canvas.removeEventListener("wheel", onWheel);
    };
  }, [rendererReady]);

  // Worker setup
  useEffect(() => {
    const worker = new Worker(new URL("./domain/generator.worker.ts", import.meta.url), {
      type: "module"
    });
    workerRef.current = worker;
    worker.onmessage = (event: MessageEvent<WorkerResult>) => {
      const msg = event.data;
      if (msg.type === "result") {
        if (msg.id !== currentRequestId.current) return;
        const data = new Float32Array(msg.buffer);
        rendererRef.current?.setStars({ data, count: msg.count });
        setStatus(`Stars: ${msg.count.toLocaleString()}`);
        setGenerating(false);
      } else if (msg.type === "error") {
        if (msg.id !== currentRequestId.current) return;
        setStatus(`Generation failed: ${msg.message}`);
        setGenerating(false);
      }
    };
    return () => {
      worker.postMessage({ type: "terminate" });
      worker.terminate();
      workerRef.current = null;
    };
  }, []);

  // Trigger generation when params change (debounced)
  useEffect(() => {
    if (!workerRef.current) return;
    const timeout = setTimeout(() => {
      const worker = workerRef.current;
      if (!worker) return;
      const requestId = currentRequestId.current + 1;
      currentRequestId.current = requestId;
      setGenerating(true);
      setStatus("Generating...");
      worker.postMessage({ type: "generate", id: requestId, params });
    }, 180);
    return () => clearTimeout(timeout);
  }, [params]);

  const presetOptions = useMemo(() => presets.map((p) => p.name), []);

  const updateParam = (key: keyof GalaxyParameters, value: number) => {
    setParams((prev) => ({ ...prev, [key]: value }));
  };

  const loadPreset = (name: string) => {
    const preset = findPreset(name);
    if (!preset) return;
    // Preserve current seed like the desktop app
    preset.seed = params.seed;
    setPresetName(name);
    setParams(preset);
  };

  const randomizeSeed = () => {
    const seed = Math.floor(Math.random() * 1_000_000);
    setParams((prev) => ({ ...prev, seed }));
  };

  const resetDefault = () => {
    setPresetName("Default");
    setParams({ ...defaultParameters, seed: params.seed });
  };

  return (
    <div className="page">
      <header className="masthead">
        <div>
          <div className="eyebrow">Galaxy Viewer</div>
          <h1>Web Client</h1>
          <p className="lede">
            Interactive browser build that mirrors the WinForms app: WebGL2 rendering, presets, and
            scrubbable controls.
          </p>
          <div className="status">
            {status}
            {generating ? " - working..." : ""}
          </div>
        </div>
        <div className="badge">web</div>
      </header>

      <div className="layout">
        <section className="panel">
          <div className="panel-heading">Viewport</div>
          <div className="canvas-shell">
            <canvas ref={canvasRef} className="viewport" />
            <div className="hint">Drag to orbit - Scroll to zoom</div>
          </div>
        </section>

        <section className="panel controls-panel">
          <div className="panel-heading">Controls</div>
          <div className="controls-scroll">
            <div className="scrub-hint">
              <span className="scrub-handle" aria-hidden="true">
                {"<>"}
              </span>
              <div className="scrub-text">
                Drag any label to scrub values. Hold Shift for 10x steps and Alt for 0.1x precision.
              </div>
            </div>

            <div className="toolbar">
              <div className="stack">
                <label className="small-label">Preset</label>
                <select
                  value={presetName}
                  onChange={(e) => loadPreset(e.target.value)}
                  className="select"
                >
                  {presetOptions.map((name) => (
                    <option key={name}>{name}</option>
                  ))}
                </select>
              </div>
              <div className="stack">
                <label className="small-label">Theme</label>
                <div className="chip-row">
                  {Object.keys(themes).map((name) => (
                    <button
                      key={name}
                      className={`chip ${theme === name ? "chip-active" : ""}`}
                      onClick={() => setTheme(name as ThemeName)}
                    >
                      {name}
                    </button>
                  ))}
                </div>
              </div>

              <div className="stack">
                <label className="small-label">Seed</label>
                <div className="seed-row">
                  <input
                    type="number"
                    value={params.seed}
                    className="input"
                    onChange={(e) =>
                      updateParam("seed", clampNumber(parseInt(e.target.value, 10), 0, 1_000_000))
                    }
                  />
                  <button className="btn ghost" onClick={randomizeSeed}>
                    Randomize
                  </button>
                </div>
              </div>
              <div className="stack">
                <label className="small-label">Actions</label>
                <div className="chip-row">
                  <button className="btn secondary" onClick={resetDefault}>
                    Reset defaults
                  </button>
                  <button className="btn secondary" onClick={() => setParams((p) => ({ ...p }))}>
                    Refresh
                  </button>
                </div>
              </div>
            </div>

            <div className="controls-grid">
              <Section title="Galaxy disk">
                <NumericField
                  label="Star count"
                  value={params.starCount}
                  min={1000}
                  max={5_000_000}
                  step={10_000}
                  decimals={0}
                  onChange={(v) => updateParam("starCount", v)}
                />
                <NumericField
                  label="Arm count"
                  value={params.armCount}
                  min={1}
                  max={8}
                  step={1}
                  decimals={0}
                  onChange={(v) => updateParam("armCount", v)}
                />
                <NumericField
                  label="Arm twist"
                  value={params.armTwist}
                  min={0}
                  max={12}
                  step={0.1}
                  decimals={1}
                  onChange={(v) => updateParam("armTwist", v)}
                />
                <NumericField
                  label="Arm spread"
                  value={params.armSpread}
                  min={0}
                  max={1}
                  step={0.01}
                  decimals={2}
                  onChange={(v) => updateParam("armSpread", v)}
                />
                <NumericField
                  label="Disk radius"
                  value={params.diskRadius}
                  min={5}
                  max={120}
                  step={0.5}
                  decimals={1}
                  onChange={(v) => updateParam("diskRadius", v)}
                />
                <NumericField
                  label="Vertical thickness"
                  value={params.verticalThickness}
                  min={0}
                  max={5}
                  step={0.05}
                  decimals={2}
                  onChange={(v) => updateParam("verticalThickness", v)}
                />
              </Section>

              <Section title="Noise & light">
                <NumericField
                  label="Noise"
                  value={params.noise}
                  min={0}
                  max={1}
                  step={0.01}
                  decimals={2}
                  onChange={(v) => updateParam("noise", v)}
                />
                <NumericField
                  label="Core falloff"
                  value={params.coreFalloff}
                  min={0.1}
                  max={6}
                  step={0.1}
                  decimals={2}
                  onChange={(v) => updateParam("coreFalloff", v)}
                />
                <NumericField
                  label="Brightness"
                  value={params.brightness}
                  min={0.1}
                  max={2.5}
                  step={0.05}
                  decimals={2}
                  onChange={(v) => updateParam("brightness", v)}
                />
              </Section>

              <Section title="Bulge">
                <NumericField
                  label="Bulge radius"
                  value={params.bulgeRadius}
                  min={0.1}
                  max={20}
                  step={0.1}
                  decimals={1}
                  onChange={(v) => updateParam("bulgeRadius", v)}
                />
                <NumericField
                  label="Bulge star count"
                  value={params.bulgeStarCount}
                  min={0}
                  max={100000}
                  step={1000}
                  decimals={0}
                  onChange={(v) => updateParam("bulgeStarCount", v)}
                />
                <NumericField
                  label="Bulge falloff"
                  value={params.bulgeFalloff}
                  min={0.5}
                  max={10}
                  step={0.1}
                  decimals={1}
                  onChange={(v) => updateParam("bulgeFalloff", v)}
                />
                <NumericField
                  label="Bulge vertical scale"
                  value={params.bulgeVerticalScale}
                  min={0.1}
                  max={4}
                  step={0.05}
                  decimals={2}
                  onChange={(v) => updateParam("bulgeVerticalScale", v)}
                />
                <NumericField
                  label="Bulge brightness"
                  value={params.bulgeBrightness}
                  min={0.1}
                  max={6}
                  step={0.05}
                  decimals={2}
                  onChange={(v) => updateParam("bulgeBrightness", v)}
                />
              </Section>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="section">
      <div className="section-title">{title}</div>
      <div className="field-grid">{children}</div>
    </div>
  );
}

function NumericField({
  label,
  value,
  min,
  max,
  step,
  decimals,
  onChange
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  decimals: number;
  onChange: (value: number) => void;
}) {
  const [draft, setDraft] = useState(value);
  const startX = useRef(0);
  const startValue = useRef(value);
  const isDragging = useRef(false);

  useEffect(() => setDraft(value), [value]);

  const commit = (next: number) => {
    const rounded = roundTo(clampNumber(next, min, max), decimals);
    setDraft(rounded);
    onChange(rounded);
  };

  const handleInput = (e: React.ChangeEvent<HTMLInputElement>) => {
    const parsed = parseFloat(e.target.value);
    if (Number.isNaN(parsed)) {
      setDraft(value);
      return;
    }
    commit(parsed);
  };

  const handleStep = (delta: number) => {
    commit(draft + delta * step);
  };

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    startX.current = e.clientX;
    startValue.current = draft;
    isDragging.current = true;
    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", handlePointerUp);
  };

  const handlePointerMove = (e: PointerEvent) => {
    if (!isDragging.current) return;
    const delta = e.clientX - startX.current;
    const next = startValue.current + delta * step * scrubMultiplier(e);
    commit(next);
  };

  const handlePointerUp = () => {
    isDragging.current = false;
    window.removeEventListener("pointermove", handlePointerMove);
    window.removeEventListener("pointerup", handlePointerUp);
  };

  useEffect(
    () => () => {
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", handlePointerUp);
    },
    []
  );

  return (
    <label className="field">
      <div
        className="field-label"
        onPointerDown={handlePointerDown}
        title="Drag to scrub. Shift=10x, Alt=0.1x."
      >
        <span className="scrub-handle" aria-hidden="true">
          {"<>"}
        </span>
        <span className="field-label-text">{label}</span>
        <span className="scrub-pill" aria-hidden="true">
          drag
        </span>
      </div>
      <div className="field-input">
        <input
          type="number"
          value={draft}
          min={min}
          max={max}
          step={step}
          onChange={handleInput}
          className="input"
        />
        <div className="steppers">
          <button type="button" onClick={() => handleStep(1)} aria-label="Increment">
            +
          </button>
          <button type="button" onClick={() => handleStep(-1)} aria-label="Decrement">
            –
          </button>
        </div>
      </div>
    </label>
  );
}

function clampNumber(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

function roundTo(value: number, decimals: number) {
  const factor = Math.pow(10, decimals);
  return Math.round(value * factor) / factor;
}
