import { useEffect, useMemo, useRef, useState } from "react";
import { defaultParameters, GalaxyParameters } from "@domain/parameters";
import { generateStars } from "@domain/generator";
import { GalaxyRenderer } from "@gl/renderer";
import "./styles.css";

export default function App() {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const rendererRef = useRef<GalaxyRenderer | null>(null);
  const [params] = useState<GalaxyParameters>({ ...defaultParameters });
  const [status, setStatus] = useState("Ready");

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const renderer = new GalaxyRenderer(canvas);
    rendererRef.current = renderer;
    renderer.init();

    const controller = new AbortController();
    setStatus("Generating stars...");
    generateStars(params, controller.signal)
      .then((stars) => {
        if (controller.signal.aborted) return;
        renderer.setStars(stars);
        renderer.render();
        setStatus(`Stars: ${stars.count.toLocaleString()}`);
      })
      .catch((err) => {
        if (controller.signal.aborted) return;
        console.error(err);
        setStatus("Failed to generate stars");
      });

    const handleResize = () => {
      renderer.resize();
      renderer.render();
    };
    window.addEventListener("resize", handleResize);

    return () => {
      controller.abort();
      window.removeEventListener("resize", handleResize);
      renderer.dispose();
    };
  }, [params]);

  const presetSummary = useMemo(
    () =>
      Object.entries(params)
        .map(([k, v]) => `${k}: ${v}`)
        .join(" | "),
    [params]
  );

  return (
    <div className="page">
      <header className="masthead">
        <div>
          <div className="eyebrow">Preview scaffold</div>
          <h1>Galaxy Viewer — Web</h1>
          <p className="lede">
            A starter web client that mirrors the desktop app: WebGL2 point cloud
            rendering plus the same galaxy math.
          </p>
          <div className="status">{status}</div>
        </div>
        <div className="badge">web prototype</div>
      </header>

      <section className="panel">
        <div className="panel-heading">Galaxy viewport</div>
        <canvas ref={canvasRef} className="viewport" />
        <div className="hint">Drag to orbit (mouse), scroll to zoom.</div>
      </section>

      <section className="panel">
        <div className="panel-heading">Current parameters</div>
        <div className="parameters">{presetSummary}</div>
        <p className="note">
          Full UI (scrubbables, presets, themes) can be layered on next. This keeps
          the renderer/generator isolated from the app shell.
        </p>
      </section>
    </div>
  );
}
