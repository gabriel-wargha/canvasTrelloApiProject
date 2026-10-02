// The dashboard: asks the app's API with fetch and draws the answers.
// Every piece of text goes in with textContent (never innerHTML), so an assignment name can't break the page.

let summary = null;   // the last /api/summary answer (preview mode, board name, courses)

// Each course gets one color, used for its highlighter stroke, its dot in the to-do list and its Trello cards
// The colors are CSS variables (--series-1 to 6), so dark mode can use its own shades
const colorCount = 6;
const courseInfo = new Map();   // course code -> { name, color }

// ---------- Small helpers ----------

// Makes an element: el("p", "quiet", "No cards")
function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = text;
  return node;
}

// A link that opens in a new tab. Only http(s) addresses are allowed.
function link(url, text, className) {
  const a = el("a", className, text);
  if (url && /^https?:\/\//.test(url)) {
    a.href = url;
    a.target = "_blank";
    a.rel = "noopener";
  }
  return a;
}

// Asks the API. On an error (like 502) it throws with the server's message.
async function api(path, options) {
  const response = await fetch(path, options);
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(data?.error ?? `The server answered ${response.status}`);
  return data;
}

function showError(boxId, what, error) {
  document.getElementById(boxId).replaceChildren(el("p", "error", `Couldn't load ${what}: ${error.message}`));
}

function formatTime(iso) {
  return new Date(iso).toLocaleString([], { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
}

function plural(count, word) {
  return `${count} ${word}${count === 1 ? "" : "s"}`;
}

function colorFor(code) {
  return courseInfo.get(code)?.color ?? "var(--pencil)";
}

// A small colored dot plus the course's readable name
function courseTag(code) {
  const tag = el("span", "course-tag");
  const dot = el("span", "dot");
  dot.style.setProperty("--course", colorFor(code));
  tag.append(dot, courseInfo.get(code)?.name ?? code);
  return tag;
}

// A progress circle: a faint full ring (the track) and a colored arc for the done part.
// SVG elements need createElementNS, not createElement.
function progressRing(done, total, label) {
  const svgNs = "http://www.w3.org/2000/svg";
  const make = (tag, attrs) => {
    const node = document.createElementNS(svgNs, tag);
    for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
    return node;
  };

  const radius = 42;
  const length = 2 * Math.PI * radius;        // the full circle's length
  const part = total === 0 ? 0 : done / total;

  const svg = make("svg", { class: "ring", viewBox: "0 0 100 100", role: "img" });
  svg.setAttribute("aria-label", `${label}: ${done} of ${total} done`);

  // Hovering the circle shows this text
  const tip = make("title", {});
  tip.textContent = `${label}: ${done} of ${total} done (${Math.round(part * 100)}%)`;

  const track = make("circle", { class: "ring-track", cx: 50, cy: 50, r: radius, "stroke-width": 9 });

  // The arc starts at the top (rotate -90°) and is drawn with a dash as long as the done part
  const fill = make("circle", {
    class: "ring-fill", cx: 50, cy: 50, r: radius, "stroke-width": 9,
    transform: "rotate(-90 50 50)",
    "stroke-dasharray": length,
    "stroke-dashoffset": length,   // starts empty, then grows (see below)
  });
  if (part === 0) fill.style.display = "none";   // a round line cap would draw a dot at 0%

  const value = make("text", { class: "ring-value", x: 50, y: 50, "text-anchor": "middle" });
  value.textContent = String(done);
  const totalText = make("text", { class: "ring-total", x: 50, y: 66, "text-anchor": "middle" });
  totalText.textContent = `of ${total}`;

  svg.append(tip, track, fill, value, totalText);

  // Next frame: set the real offset, so the CSS transition animates the arc growing
  requestAnimationFrame(() => fill.setAttribute("stroke-dashoffset", String(length * (1 - part))));
  return svg;
}

// ---------- The sections ----------

function drawSummary(data) {
  summary = data;
  courseInfo.clear();
  data.courses.forEach((course, i) =>
    courseInfo.set(course.code, { name: course.name.trim() || course.code, color: `var(--series-${(i % colorCount) + 1})` }));

  // Toolbar: preview mode and the button's name
  document.getElementById("mode").hidden = !data.dryRun;
  document.querySelector("#sync-button .label").textContent = data.dryRun ? "Preview sync" : "Sync with Trello";
  document.getElementById("confirm-board").textContent = data.boardName;

  // Headline: the one question the page answers
  const left = data.total - data.done;
  document.getElementById("headline").textContent =
    left === 0 ? "Everything is done." : `${plural(left, "assignment")} to do`;

  document.getElementById("subline").textContent =
    data.lastSync ? `Last synced ${formatTime(data.lastSync)}` : "Not synced yet";

  const box = document.getElementById("courses");
  if (data.courses.length === 0) {
    box.replaceChildren(el("p", "quiet", "None of your courses has assignments yet."));
    return;
  }

  const grid = el("div", "course-grid");
  for (const course of data.courses) {
    const info = courseInfo.get(course.code);

    // The Canvas code is long, so it only shows on hover
    const name = el("div", "course-name", info.name);
    name.title = course.code;

    const tile = el("div", "course");
    tile.style.setProperty("--course", info.color);
    tile.append(progressRing(course.done, course.total, info.name), name,
      el("div", "course-sub", course.done === course.total ? "All done" : `${course.total - course.done} left`));
    grid.append(tile);
  }
  box.replaceChildren(grid);
}

function drawTodo(assignments) {
  const todo = assignments.filter(a => !a.submitted);
  document.getElementById("todo-count").textContent = String(todo.length);

  const box = document.getElementById("todo");
  if (todo.length === 0) {
    box.replaceChildren(el("p", "quiet", "Nothing left. Every assignment is submitted."));
    return;
  }

  // Grouped by course, so each course name shows once instead of on every line
  const groups = new Map();
  for (const a of todo) {
    if (!groups.has(a.course)) groups.set(a.course, []);
    groups.get(a.course).push(a);
  }

  const wrap = el("div", "todo-groups");
  for (const [course, items] of groups) {
    const heading = el("h3", "todo-course");
    heading.append(courseTag(course));

    const list = el("ul", "todo-list");
    for (const a of items) {
      // The whole row is one link to the assignment on Canvas
      const row = link(a.canvasUrl, null, "todo-row");
      row.append(el("span", "box"), el("span", "todo-name", a.name), el("span", "todo-go", "Canvas ↗"));

      const item = el("li");
      item.append(row);
      if (a.cardUrl) item.append(link(a.cardUrl, "Trello ↗", "todo-trello"));
      list.append(item);
    }

    wrap.append(heading, list);
  }
  box.replaceChildren(wrap);
}

// The Next tab: one card per course with the assignment to do next and its points
function drawNext(items) {
  const box = document.getElementById("next");
  if (items.length === 0) {
    box.replaceChildren(el("p", "quiet", "Nothing left. Every assignment is done."));
    return;
  }

  const grid = el("div", "next-grid");
  for (const item of items) {
    const card = el("article", "next-item");
    card.style.setProperty("--course", colorFor(item.course));

    const heading = el("h3", "todo-course");
    heading.append(courseTag(item.course));

    const points = item.points === null || item.points === undefined
      ? "No points"
      : plural(item.points, "point");

    const links = el("p", "next-links");
    links.append(link(item.canvasUrl, "Open in Canvas ↗"));
    if (item.cardUrl) links.append(link(item.cardUrl, "Trello ↗"));

    card.append(
      heading,
      el("p", "next-name", item.name),
      el("p", "next-points", points),
      links);
    grid.append(card);
  }
  box.replaceChildren(grid);
}

function drawBoard(board) {
  const lists = el("div", "lists");

  for (const list of board.lists) {
    const column = el("div", `list ${list.name.toLowerCase()}`);
    const title = el("h3", null, `${list.name} `);
    title.append(el("span", null, `(${list.cards.length})`));

    const cards = el("div", "list-cards");

    // The recommended next activity goes first. Its own card is taken out of the rest, so it shows once.
    let rest = list.cards;
    if (list.next) {
      const nextCard = list.cards.find(c => c.id === list.next.cardId);
      rest = list.cards.filter(c => c !== nextCard);

      const next = link(nextCard?.url ?? list.next.canvasUrl, null, "index-card next");
      next.append(el("span", "next-label", "⭐ Next up"), el("span", null, list.next.name));
      const course = /^\[(.+?)\]/.exec(nextCard?.name ?? "");
      if (course) next.style.setProperty("--course", colorFor(course[1]));
      cards.append(next);
    }

    if (list.cards.length === 0 && !list.next) {
      cards.append(el("p", "empty-list", "Empty"));
    } else {
      for (const card of rest) {
        // Our cards are named "[COURSE] Assignment": show the name, and use the course for the color
        const match = /^\[(.+?)\]\s*(.*)$/.exec(card.name);
        const node = link(card.url, match ? match[2] : card.name, "index-card");
        if (match) node.style.setProperty("--course", colorFor(match[1]));
        cards.append(node);
      }
    }

    column.append(title, cards);
    lists.append(column);
  }

  document.getElementById("board").replaceChildren(lists);
}

function drawHistory(history) {
  const box = document.getElementById("history");
  if (history.length === 0) {
    box.replaceChildren(el("p", "quiet", "No syncs yet."));
    return;
  }

  const list = el("ul", "log-list");
  for (const run of history) {
    const what = el("div", "log-what");
    what.append(el("b", null, `+${run.created}`), "   ", el("b", "moved", `${run.moved} to Done`));

    const item = el("li");
    item.append(
      el("div", "log-when", formatTime(run.ranAt)),
      el("div", "log-from", run.trigger),
      what);
    list.append(item);
  }
  box.replaceChildren(list);
}

// Starts all four requests at once. The to-do list and the board wait for the summary,
// because they use its course names and colors. One failing section shows its own error.
async function loadAll() {
  const summaryRequest = api("/api/summary");
  const requests = {
    assignments: api("/api/assignments"),
    board: api("/api/board"),
    next: api("/api/next"),
    history: api("/api/history"),
  };

  try {
    drawSummary(await summaryRequest);
  } catch (error) {
    document.getElementById("headline").textContent = "Couldn't reach Canvas";
    document.getElementById("subline").textContent = error.message;
    showError("courses", "your courses", error);
  }

  await Promise.all([
    requests.assignments.then(drawTodo, error => showError("todo", "your assignments", error)),
    requests.board.then(drawBoard, error => showError("board", "the Trello board", error)),
    requests.next.then(drawNext, error => showError("next", "what to do next", error)),
    requests.history.then(drawHistory, error => showError("history", "the sync log", error)),
  ]);
}

// ---------- Sync ----------

// Asks inside the page (not a browser popup). Resolves true for "Sync with Trello".
function askToConfirm() {
  const dialog = document.getElementById("confirm");
  return new Promise(resolve => {
    const answer = value => {
      dialog.close();
      resolve(value);
    };
    document.getElementById("confirm-yes").onclick = () => answer(true);
    document.getElementById("confirm-no").onclick = () => answer(false);
    dialog.oncancel = () => resolve(false);   // Esc key
    dialog.showModal();
  });
}

function drawResult(result) {
  const box = document.getElementById("result");
  box.className = result.dryRun ? "result preview" : "result";

  const created = result.created.length;
  const moved = result.moved.length;
  const regrouped = result.regrouped.length;
  const main = result.dryRun
    ? `Preview: would create ${created}, move ${moved}, regroup ${regrouped}. Nothing changed.`
    : `Synced: created ${created}, moved ${moved}, regrouped ${regrouped}.`;
  box.replaceChildren(el("p", "result-main", main));

  const changes = [
    ...result.created.map(a => `New card: ${a.name} (${courseInfo.get(a.course)?.name ?? a.course})`),
    ...result.moved.map(a => `Moved to Done: ${a.name} (${courseInfo.get(a.course)?.name ?? a.course})`),
    ...result.regrouped.map(a => `Moved to its course list: ${a.name} (${courseInfo.get(a.course)?.name ?? a.course})`),
  ];
  if (changes.length > 0) {
    const details = el("details");
    details.append(el("summary", null, `Show the ${plural(changes.length, "card")}`));
    const list = el("ul");
    for (const change of changes) list.append(el("li", null, change));
    details.append(list);
    box.append(details);
  }

  if (result.failedCourses.length > 0)
    box.append(el("p", null, `Canvas blocked these courses, so they were skipped: ${result.failedCourses.join(", ")}.`));

  box.hidden = false;
}

async function syncNow() {
  const button = document.getElementById("sync-button");
  const label = button.querySelector(".label");
  const idleText = label.textContent;
  const preview = summary?.dryRun === true;

  // A real sync changes the real board, so ask first. Only skip the question when we know for sure it's a
  // preview: if the summary failed to load, we don't know, so we ask.
  if (!preview && !(await askToConfirm()))
    return;

  button.disabled = true;
  button.classList.add("busy");
  label.textContent = preview ? "Previewing…" : "Syncing…";

  try {
    drawResult(await api("/api/sync", { method: "POST" }));

    // Reloading reads Canvas again and takes a few seconds: fade the sections and say so,
    // so the old board (and its old Next up star) doesn't look final
    const updating = el("p", "updating-note", "Updating the page…");
    document.getElementById("result").append(updating);
    document.querySelector("main").classList.add("updating");
    try {
      await loadAll();
    } finally {
      updating.remove();
      document.querySelector("main").classList.remove("updating");
    }
  } catch (error) {
    const box = document.getElementById("result");
    box.className = "result failed";
    box.replaceChildren(el("p", "result-main", `Sync stopped: ${error.message}`));
    box.hidden = false;
  } finally {
    button.disabled = false;
    button.classList.remove("busy");
    label.textContent = idleText;
  }
}

// ---------- Tabs ----------

const views = ["overview", "next", "todo", "board", "log"];

// Shows one section and hides the others. The tab name goes in the address (#todo),
// so reloading the page keeps you on the same tab.
function showView(name) {
  if (!views.includes(name)) name = "overview";

  for (const view of views)
    document.getElementById(`view-${view}`).hidden = view !== name;

  for (const tab of document.querySelectorAll(".tabs [role=tab]")) {
    const active = tab.dataset.view === name;
    tab.setAttribute("aria-selected", String(active));
    tab.tabIndex = active ? 0 : -1;
  }

  history.replaceState(null, "", name === "overview" ? location.pathname : `#${name}`);
}

function setUpTabs() {
  const tabs = [...document.querySelectorAll(".tabs [role=tab]")];
  for (const tab of tabs) {
    tab.addEventListener("click", () => showView(tab.dataset.view));

    // Arrow keys move between tabs, like in any tab bar
    tab.addEventListener("keydown", event => {
      const step = event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
      if (step === 0) return;
      const next = tabs[(tabs.indexOf(tab) + step + tabs.length) % tabs.length];
      showView(next.dataset.view);
      next.focus();
    });
  }
  showView(location.hash.slice(1));
}

// ---------- Light / dark ----------

function currentTheme() {
  const picked = document.documentElement.dataset.theme;
  if (picked) return picked;
  return matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

function drawThemeButton() {
  const button = document.getElementById("theme-button");
  const next = currentTheme() === "dark" ? "light" : "dark";
  button.setAttribute("aria-label", `Switch to ${next} mode`);
  button.title = `Switch to ${next} mode`;
}

function toggleTheme() {
  const next = currentTheme() === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = next;
  try { localStorage.setItem("theme", next); } catch { /* storage blocked: works until reload */ }
  drawThemeButton();
}

// ---------- Start ----------

document.getElementById("sync-button").addEventListener("click", syncNow);
document.getElementById("theme-button").addEventListener("click", toggleTheme);
drawThemeButton();
setUpTabs();
loadAll();
