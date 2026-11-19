import type { ReactNode } from 'react'

type PillProps = {
  children: ReactNode
  onClick?: () => void
  pressed?: boolean
  disabled?: boolean
  onRemove?: () => void
  removeLabel?: string
  className?: string
}

export function Pill({ children, onClick, pressed, disabled, onRemove, removeLabel = 'Remove', className = '' }: PillProps) {
  const classes = `pill${onClick ? ' pill-action' : ''}${pressed ? ' pill-pressed' : ''}${className ? ` ${className}` : ''}`

  if (onClick) {
    return <button type="button" className={classes} aria-pressed={pressed} disabled={disabled} onClick={onClick}>{children}</button>
  }

  return <span className={classes}>
    {children}
    {onRemove && <button type="button" className="pill-remove" aria-label={removeLabel} disabled={disabled} onClick={onRemove}>×</button>}
  </span>
}
