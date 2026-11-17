// Header badges (refreshed every 30 seconds) and optional browser notifications.
// Plain JavaScript. Browser notifications work while a dashboard tab is open.
(function () {
  "use strict";

  var body = document.body;
  var url = body.getAttribute("data-alerts-url");
  if (!url) return;

  var INTERVAL_MS = 30000; // how often the page asks the server
  var userId = body.getAttribute("data-user-id") || "0";
  var KEY_PROMPTED = "alerts.prompted." + userId; // the banner was answered
  var KEY_ENABLED = "alerts.enabled." + userId; // 'on' or 'off' (the bell switch)
  var KEY_LAST = "alerts.last." + userId; // the previous numbers

  var supported = "Notification" in window;
  var banner = document.getElementById("notif-banner");
  var toggle = document.getElementById("notif-toggle");

  function read(key) {
    try {
      return window.localStorage.getItem(key);
    } catch (e) {
      return null;
    }
  }
  function write(key, value) {
    try {
      window.localStorage.setItem(key, value);
    } catch (e) {
      /* private mode */
    }
  }

  function toast(text) {
    var old = document.querySelector(".alerts-toast");
    if (old) old.remove();
    var el = document.createElement("div");
    el.className = "alerts-toast";
    el.setAttribute("role", "status");
    el.textContent = text;
    document.body.appendChild(el);
    window.setTimeout(function () {
      el.remove();
    }, 6000);
  }

  function permission() {
    return supported ? Notification.permission : "denied";
  }

  function notificationsOn() {
    return permission() === "granted" && read(KEY_ENABLED) !== "off";
  }

  // ---- Bell + banner state ----
  function render() {
    if (banner) {
      banner.hidden = !(
        supported &&
        permission() === "default" &&
        !read(KEY_PROMPTED)
      );
    }
    if (toggle) {
      toggle.classList.remove("is-on", "is-off", "is-blocked");
      var state =
        !supported || permission() === "denied"
          ? "blocked"
          : notificationsOn()
            ? "on"
            : "off";
      toggle.classList.add("is-" + state);
      toggle.setAttribute("aria-pressed", state === "on" ? "true" : "false");
      toggle.title =
        state === "on"
          ? "Notifications are on. Click to turn off."
          : state === "blocked"
            ? "Notifications are blocked in the browser."
            : "Notifications are off. Click to turn on.";
    }
  }

  function ask() {
    if (!supported) {
      toast("This browser does not support notifications.");
      return;
    }
    write(KEY_PROMPTED, "1");
    var done = function (result) {
      if (result === "granted") {
        write(KEY_ENABLED, "on");
        toast("Notifications are on.");
      } else if (result === "denied") {
        toast(
          "Notifications are blocked. Allow them in the browser's site settings (icon left of the address).",
        );
      }
      render();
    };
    try {
      var promise = Notification.requestPermission(done);
      if (promise && typeof promise.then === "function") promise.then(done);
    } catch (e) {
      render();
    }
  }

  var enableButton = document.getElementById("notif-enable");
  var laterButton = document.getElementById("notif-later");
  if (enableButton) enableButton.addEventListener("click", ask);
  if (laterButton)
    laterButton.addEventListener("click", function () {
      write(KEY_PROMPTED, "1");
      render();
    });

  if (toggle) {
    toggle.addEventListener("click", function () {
      var p = permission();
      if (!supported || p === "denied") {
        toast(
          "Notifications are blocked. Allow them in the browser's site settings (icon left of the address).",
        );
      } else if (p === "default") {
        ask();
      } else if (notificationsOn()) {
        write(KEY_ENABLED, "off");
        toast("Notifications are off.");
        render();
      } else {
        write(KEY_ENABLED, "on");
        toast("Notifications are on.");
        render();
      }
    });
  }

  // ---- Badges ----
  var chips = {};
  document.querySelectorAll("[data-alert]").forEach(function (el) {
    chips[el.getAttribute("data-alert")] = el;
  });

  var texts = {
    unassigned: function (n) {
      return (
        n + (n === 1 ? " new unassigned report" : " new unassigned reports")
      );
    },
    needsTriage: function (n) {
      return n + (n === 1 ? " report needs triage" : " reports need triage");
    },
    stale: function (n) {
      return n + (n === 1 ? " report is stale" : " reports are stale");
    },
  };

  function showCounts(counts) {
    Object.keys(chips).forEach(function (key) {
      var n = counts[key] || 0;
      var chip = chips[key];
      var number = chip.querySelector(".alert-count");
      if (number) number.textContent = n;
      chip.classList.toggle("has-alerts", n > 0);
    });
  }

  function notifyAboutIncreases(counts) {
    var previous = null;
    try {
      previous = JSON.parse(read(KEY_LAST) || "null");
    } catch (e) {
      previous = null;
    }
    write(KEY_LAST, JSON.stringify(counts));
    if (!previous || !notificationsOn()) return; // the first check only remembers the numbers

    var lines = [];
    var target = null;
    Object.keys(chips).forEach(function (key) {
      var more = (counts[key] || 0) - (previous[key] || 0);
      if (more > 0 && texts[key]) {
        lines.push(texts[key](more));
        target = target || chips[key].getAttribute("href");
      }
    });
    if (lines.length === 0) return;

    try {
      var note = new Notification("Sarajevo Reports", {
        body: lines.join("\n"),
        tag: "sarajevo-reports-alerts",
      });
      note.onclick = function () {
        window.focus();
        if (target) window.location.href = target;
        note.close();
      };
    } catch (e) {
      /* some browsers only allow this from a service worker */
    }
  }

  var autoReload = body.hasAttribute("data-autoreload");
  var lastOpen = null;

  function bindRows(scope) {
    scope.querySelectorAll("tr[data-href]").forEach(function (row) {
      row.addEventListener("click", function (e) {
        if (e.target.closest("a, button, input, select, form")) return;
        window.location.href = row.getAttribute("data-href");
      });
    });
  }

  function refreshContent() {
    fetch(window.location.href, {
      credentials: "same-origin",
      headers: { Accept: "text/html" },
    })
      .then(function (response) {
        if (!response.ok) throw new Error("HTTP " + response.status);
        return response.text();
      })
      .then(function (html) {
        var doc = new DOMParser().parseFromString(html, "text/html");
        var fresh = doc.querySelector("main.dash-main");
        var current = document.querySelector("main.dash-main");
        if (!fresh || !current) return; // for example a login page: leave everything as it is

        var keep = current.querySelector("#notif-banner");
        Array.prototype.slice.call(current.children).forEach(function (el) {
          if (el !== keep) el.remove();
        });
        Array.prototype.slice.call(fresh.children).forEach(function (el) {
          if (el.id === "notif-banner" || el.classList.contains("flash"))
            return;
          current.appendChild(document.importNode(el, true));
        });
        bindRows(current);
      })
      .catch(function () {
        /* try again at the next change */
      });
  }

  function reloadIfChanged(counts) {
    if (!autoReload || typeof counts.open !== "number") return;
    if (lastOpen !== null && counts.open !== lastOpen) refreshContent();
    lastOpen = counts.open;
  }

  function poll() {
    fetch(url, {
      credentials: "same-origin",
      headers: { Accept: "application/json" },
    })
      .then(function (response) {
        if (!response.ok) throw new Error("HTTP " + response.status);
        return response.json(); // a login page (expired session) is not JSON and ends in catch
      })
      .then(function (counts) {
        showCounts(counts);
        notifyAboutIncreases(counts);
        reloadIfChanged(counts);
      })
      .catch(function () {
        /* try again at the next round */
      });
  }

  render();
  poll();
  window.setInterval(poll, INTERVAL_MS);
  document.addEventListener("visibilitychange", function () {
    if (!document.hidden) poll();
  });
})();
