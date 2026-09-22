let library;
let nextId = 0;
const sizes = new WeakMap();

async function getMermaid() {
    library ??= import("./mermaid.esm.min.mjs")
        .then(({ default: mermaid }) => {
            mermaid.initialize({ startOnLoad: false, securityLevel: "strict", suppressErrorRendering: true });
            return mermaid;
        }).catch(error => {
            library = undefined;
            throw error;
        });
    return library;
}

export async function render(host, source) {
    host.replaceChildren();
    sizes.delete(host);
    if (!source?.trim()) return;
    const mermaid = await getMermaid();
    const { svg } = await mermaid.render(`gx-mermaid-${++nextId}`, source);
    if (!host.isConnected) return;
    host.innerHTML = svg;
    const diagram = host.querySelector("svg");
    const box = diagram?.viewBox.baseVal;
    if (!box || box.width <= 0 || box.height <= 0) {
        host.replaceChildren();
        throw new Error("The diagram has no drawable dimensions.");
    }
    sizes.set(host, { width: box.width, height: box.height });
    diagram.style.maxWidth = "none";
    diagram.style.display = "block";
    zoom(host, 1);
    host.parentElement.scrollTo(0, 0);
}

export function zoom(host, scale) {
    const size = sizes.get(host);
    const diagram = host.querySelector("svg");
    if (!size || !diagram) return;
    scale = Math.min(4, Math.max(0.1, scale));
    // Explicit dimensions keep the entire enlarged SVG reachable by scrolling.
    diagram.style.width = `${size.width * scale}px`;
    diagram.style.height = `${size.height * scale}px`;
}
