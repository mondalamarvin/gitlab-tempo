import { useEffect, useState } from "react";

export type Theme = "light" | "dark";

// Keep in sync with the inline script in index.html, which applies the theme before the first paint.
const STORAGE_KEY = "gitlab-tempo.theme";

/** The saved choice, or the OS setting if the user has never picked one. */
function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === "light" || saved === "dark") return saved;
  } catch {
    /* storage unavailable */
  }
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/** Current theme plus a toggle that flips it, saves the choice and updates the page. */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(initialTheme);

  useEffect(() => {
    document.documentElement.classList.toggle("dark", theme === "dark");
  }, [theme]);

  const toggle = () =>
    setTheme((current) => {
      const next = current === "dark" ? "light" : "dark";
      try {
        localStorage.setItem(STORAGE_KEY, next);
      } catch {
        /* storage unavailable: the choice lasts for this tab only */
      }
      return next;
    });

  return [theme, toggle] as const;
}
