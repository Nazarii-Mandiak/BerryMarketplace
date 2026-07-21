import type { ReactNode } from 'react';

const INK = 'var(--ink)';
const LEAF = '#3f5b3c';

function Calyx({ cx, cy, fill }: { cx: number; cy: number; fill: string }) {
  return (
    <g transform={`translate(${cx},${cy})`}>
      {[-40, -20, 0, 20, 40].map((deg) => (
        <ellipse
          key={deg}
          cx={0}
          cy={-7}
          rx={6.5}
          ry={deg === 0 ? 18 : 16}
          fill={fill}
          stroke={INK}
          strokeWidth={4}
          transform={`rotate(${deg})`}
        />
      ))}
    </g>
  );
}

function Drupelets({ fill }: { fill: string }) {
  const rows = [
    { y: 90, xs: [78, 100, 122] },
    { y: 111, xs: [64, 87, 110, 133] },
    { y: 132, xs: [72, 95, 118, 140] },
    { y: 151, xs: [84, 107, 129] },
  ];
  return (
    <>
      {rows.map((row) =>
        row.xs.map((x) => (
          <circle key={`${row.y}-${x}`} cx={x} cy={row.y} r={13} fill={fill} stroke={INK} strokeWidth={2} />
        )),
      )}
    </>
  );
}

function BaseLeaf({ x, y }: { x: number; y: number }) {
  const d = `M ${x} ${y} C ${x - 16} ${y - 4} ${x - 20} ${y - 18} ${x - 10} ${y - 28} C ${x - 1} ${y - 16} ${x - 2} ${y - 4} ${x} ${y} Z`;
  return <path d={d} fill={LEAF} stroke={INK} strokeWidth={4} />;
}

function Wrap({ children }: { children: ReactNode }) {
  return (
    <svg className="berry-icon" viewBox="0 0 200 200" role="img" aria-hidden="true">
      {children}
    </svg>
  );
}

const ICON_BUILDERS: Record<string, () => ReactNode> = {
  strawberries: () => {
    const seeds: [number, number, number][] = [
      [78, 90, -10], [104, 82, 15], [128, 98, -20], [70, 118, 5], [96, 112, -5],
      [122, 124, 20], [82, 146, -15], [108, 150, 10], [60, 96, 25], [138, 118, -8],
    ];
    return (
      <>
        <path
          d="M100,56 C132,50 162,76 158,108 C154,144 128,178 100,178 C72,178 46,144 42,108 C38,76 68,50 100,56 Z"
          fill="#e5384f"
          stroke={INK}
          strokeWidth={6}
        />
        {seeds.map(([x, y, rot]) => (
          <ellipse key={`${x}-${y}`} cx={x} cy={y} rx={4.5} ry={7} fill="#f5d77a" transform={`rotate(${rot} ${x} ${y})`} />
        ))}
        <Calyx cx={100} cy={52} fill={LEAF} />
      </>
    );
  },
  blueberries: () => (
    <>
      <circle cx={100} cy={112} r={56} fill="#4c5fa8" stroke={INK} strokeWidth={6} />
      <ellipse cx={78} cy={90} rx={16} ry={10} fill="#ffffff" opacity={0.35} transform="rotate(-20 78 90)" />
      <path
        d="M100,60 L94,47 M100,60 L100,44 M100,60 L106,47 M100,60 L90,54 M100,60 L110,54"
        stroke="#22284a"
        strokeWidth={4}
        strokeLinecap="round"
        fill="none"
      />
      <line x1={100} y1={44} x2={100} y2={32} stroke={LEAF} strokeWidth={5} strokeLinecap="round" />
    </>
  ),
  raspberries: () => (
    <>
      <path
        d="M60,150 C55,98 70,58 100,56 C130,58 145,98 140,150 C140,169 120,180 100,180 C80,180 60,169 60,150 Z"
        fill="#e8637a"
        stroke={INK}
        strokeWidth={6}
      />
      <Drupelets fill="#f3a6b4" />
      <BaseLeaf x={52} y={158} />
    </>
  ),
  blackberries: () => (
    <>
      <path
        d="M60,150 C55,98 70,58 100,56 C130,58 145,98 140,150 C140,169 120,180 100,180 C80,180 60,169 60,150 Z"
        fill="#3b2740"
        stroke={INK}
        strokeWidth={6}
      />
      <Drupelets fill="#5c4066" />
      <BaseLeaf x={52} y={158} />
    </>
  ),
  gooseberries: () => (
    <>
      <ellipse cx={100} cy={112} rx={54} ry={60} fill="#cfe29a" fillOpacity={0.65} stroke={INK} strokeWidth={6} />
      <path d="M72,72 C87,98 87,132 72,158" fill="none" stroke="#8fae55" strokeWidth={2.5} />
      <path d="M100,62 C105,98 105,132 100,166" fill="none" stroke="#8fae55" strokeWidth={2.5} />
      <path d="M128,72 C113,98 113,132 128,158" fill="none" stroke="#8fae55" strokeWidth={2.5} />
      <ellipse cx={82} cy={86} rx={14} ry={9} fill="#ffffff" opacity={0.4} transform="rotate(-25 82 86)" />
      <line x1={100} y1={52} x2={100} y2={38} stroke={LEAF} strokeWidth={5} strokeLinecap="round" />
    </>
  ),
  mulberries: () => (
    <>
      <g transform="translate(100,116) scale(0.85,1.12) translate(-100,-116)">
        <path
          d="M60,150 C55,98 70,58 100,56 C130,58 145,98 140,150 C140,169 120,180 100,180 C80,180 60,169 60,150 Z"
          fill="#4a1e38"
          stroke={INK}
          strokeWidth={6}
        />
        <Drupelets fill="#7a3a5c" />
      </g>
      <BaseLeaf x={58} y={62} />
    </>
  ),
};

function GenericIcon(): ReactNode {
  return (
    <>
      <circle cx={100} cy={112} r={56} fill="var(--accent)" stroke={INK} strokeWidth={6} />
      <ellipse cx={78} cy={90} rx={16} ry={10} fill="#ffffff" opacity={0.3} transform="rotate(-20 78 90)" />
      <line x1={100} y1={56} x2={100} y2={40} stroke={LEAF} strokeWidth={5} strokeLinecap="round" />
      <ellipse cx={110} cy={42} rx={11} ry={6} fill={LEAF} stroke={INK} strokeWidth={3} transform="rotate(25 110 42)" />
    </>
  );
}

export function BerryIcon({ berryType }: { berryType: string }) {
  const key = berryType.toLowerCase();
  const match = Object.keys(ICON_BUILDERS).find((name) => key.includes(name.slice(0, -3)));
  const build = match ? ICON_BUILDERS[match] : GenericIcon;
  return <Wrap>{build()}</Wrap>;
}
