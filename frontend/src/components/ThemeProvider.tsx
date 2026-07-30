import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

export type Theme = 'light' | 'dark' | 'system';

interface ThemeContextValue {
  theme: Theme;
  setTheme: (theme: Theme) => void;
  resolved: 'light' | 'dark';
}

const ThemeContext = createContext<ThemeContextValue | null>(null);
const STORAGE_KEY = 'theme';
const DARK_QUERY = '(prefers-color-scheme: dark)';

function readStoredTheme(): Theme {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === 'light' || stored === 'dark' ? stored : 'system';
}

function systemPrefersDark(): boolean {
  return matchMedia(DARK_QUERY).matches;
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readStoredTheme);
  const [resolved, setResolved] = useState<'light' | 'dark'>(() =>
    theme === 'system' ? (systemPrefersDark() ? 'dark' : 'light') : theme,
  );

  useEffect(() => {
    const next = theme === 'system' ? (systemPrefersDark() ? 'dark' : 'light') : theme;
    setResolved(next);
    document.documentElement.classList.toggle('dark', next === 'dark');

    if (theme !== 'system') return;

    const media = matchMedia(DARK_QUERY);
    const onChange = () => {
      const nextResolved = media.matches ? 'dark' : 'light';
      setResolved(nextResolved);
      document.documentElement.classList.toggle('dark', nextResolved === 'dark');
    };
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [theme]);

  function setTheme(next: Theme) {
    setThemeState(next);
    if (next === 'system') {
      localStorage.removeItem(STORAGE_KEY);
    } else {
      localStorage.setItem(STORAGE_KEY, next);
    }
  }

  return <ThemeContext.Provider value={{ theme, setTheme, resolved }}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return ctx;
}
