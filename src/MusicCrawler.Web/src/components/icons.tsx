// Small inline SVG icons used by the rate / snooze / clear actions. They draw with `currentColor`
// and stroke, so they inherit the button's neon colour (and any glow comes from the button's CSS).
// Sized in px via `size`; default 18 sits well inside a .disc-btn.

type IconProps = { size?: number; className?: string }

function Svg({ size = 18, className, children }: IconProps & { children: React.ReactNode }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2.3"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  )
}

// Approve — an upward chevron/spark. Reads as "boost / yes", no boomer thumb.
export function IconApprove(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 4 5 13h4v7h6v-7h4z" />
    </Svg>
  )
}

// Reject — a downward chevron/spark, the mirror of approve.
export function IconReject(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 20 5 11h4V4h6v7h4z" />
    </Svg>
  )
}

// Snooze — a crescent moon.
export function IconMoon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M20 14.2A8 8 0 1 1 9.8 4 6.3 6.3 0 0 0 20 14.2Z" />
    </Svg>
  )
}

// Clear a rating — an eraser/backspace wedge with a cross, distinct from Reject.
export function IconClear(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 5h9a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H9L3 12z" />
      <path d="M14.5 9.5 9.5 14.5M9.5 9.5l5 5" />
    </Svg>
  )
}

// Chevron — the expand/collapse toggle for an artist's album drill-down. Points right when
// collapsed; rotate it via CSS when open.
export function IconChevron(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M9 6l6 6-6 6" />
    </Svg>
  )
}

// Check — marks an album the library already owns.
export function IconCheck(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M5 13l4 4L19 7" />
    </Svg>
  )
}

// Wrench — the "correct/fix the Deezer association" action.
export function IconWrench(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M14.7 6.3a4 4 0 0 0-5.2 5.2L4 17l3 3 5.5-5.5a4 4 0 0 0 5.2-5.2l-2.6 2.6-2.4-2.4z" />
    </Svg>
  )
}
