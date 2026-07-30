import { Monitor, Moon, Sun } from 'lucide-react';
import { useTheme, type Theme } from './ThemeProvider';

const OPTIONS: { value: Theme; label: string; Icon: typeof Sun }[] = [
  { value: 'light', label: 'Light theme', Icon: Sun },
  { value: 'dark', label: 'Dark theme', Icon: Moon },
  { value: 'system', label: 'Match system theme', Icon: Monitor },
];

export function ThemeToggle() {
  const { theme, setTheme } = useTheme();

  return (
    <div className="theme-toggle" role="group" aria-label="Theme">
      {OPTIONS.map(({ value, label, Icon }) => (
        <button
          key={value}
          type="button"
          className={`theme-toggle-btn${theme === value ? ' active' : ''}`}
          aria-pressed={theme === value}
          aria-label={label}
          title={label}
          onClick={() => setTheme(value)}
        >
          <Icon size={15} />
        </button>
      ))}
    </div>
  );
}
