import { useEffect, useState } from 'react'
import type { Dispatch, DragEvent, FormEvent, KeyboardEvent, SetStateAction } from 'react'
import { api } from '../api'
import { Button } from '../components/Button'
import type { Document, EditableIngredient, EditableStep } from '../types'

type RecipeEditorProps = {
  title: string
  setTitle: (value: string) => void
  ingredients: EditableIngredient[]
  setIngredients: Dispatch<SetStateAction<EditableIngredient[]>>
  steps: EditableStep[]
  setSteps: Dispatch<SetStateAction<EditableStep[]>>
  tags: string
  setTags: (value: string) => void
  busy: boolean
  changes: number
  original: Document | null
  warnings: string[]
  error: string
  notice: string
  setNotice: (value: string) => void
  newRowKey: () => number
  focusRow: (id: string) => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  onDiscard: () => void
}

type RowKind = 'ingredient' | 'step'
type AmountDraft = { mode: 'picker' | 'free'; quantity: string; unit: string; text: string }

const UNIT_OPTIONS = ['', 'tsp', 'tbsp', 'cup', 'g', 'kg', 'ml', 'l', 'piece', 'pinch', 'clove', 'slice', 'can', 'bunch', 'handful', 'small', 'medium', 'large', 'to taste']

const UNIT_KEYS: Record<string, string> = {
  tsp: 'tsp', teaspoon: 'tsp', teaspoons: 'tsp',
  tbsp: 'tbsp', tablespoon: 'tbsp', tablespoons: 'tbsp',
  cup: 'cup', cups: 'cup',
  g: 'g', gram: 'g', grams: 'g', kg: 'kg', ml: 'ml',
  l: 'l', liter: 'l', liters: 'l', litre: 'l', litres: 'l',
  piece: 'piece', pieces: 'piece', pinch: 'pinch', pinches: 'pinch',
  clove: 'clove', cloves: 'clove', slice: 'slice', slices: 'slice',
  can: 'can', cans: 'can', bunch: 'bunch', bunches: 'bunch',
  handful: 'handful', handfuls: 'handful',
  small: 'small', medium: 'medium', large: 'large'
}

const ALLOWED_AMOUNT = /[^0-9.,/–\-\s½¼¾⅓⅔⅛⅜⅝⅞]/g
const HAS_NUMBER = /[0-9½¼¾⅓⅔⅛⅜⅝⅞]/

function canonicalUnit(unit: string): string {
  const key = unit.trim().toLowerCase().replace(/\.$/, '')
  if (key === 'to taste') {
    return 'to taste'
  }
  return UNIT_KEYS[key] ?? ''
}

function parseAmount(amount: string): AmountDraft {
  const text = amount.trim()
  if (!text) {
    return { mode: 'picker', quantity: '', unit: '', text: '' }
  }
  if (text.toLowerCase() === 'to taste') {
    return { mode: 'picker', quantity: '', unit: 'to taste', text: '' }
  }

  const match = /^([0-9.,/\-–\s½¼¾⅓⅔⅛⅜⅝⅞]+)(.*)$/.exec(text)
  if (!match) {
    return { mode: 'free', quantity: '', unit: '', text }
  }

  const quantity = match[1].trim()
  const unit = match[2].trim()
  if (!unit) {
    return { mode: 'picker', quantity, unit: '', text: '' }
  }
  if (!canonicalUnit(unit)) {
    return { mode: 'free', quantity: '', unit: '', text }
  }
  return { mode: 'picker', quantity, unit, text: '' }
}

function composeAmount(draft: AmountDraft): string {
  if (draft.mode === 'free') {
    return draft.text
  }
  if (draft.unit === 'to taste') {
    return 'to taste'
  }

  const quantity = draft.quantity.trim()
  if (!quantity || !HAS_NUMBER.test(quantity)) {
    return ''
  }
  return draft.unit ? `${quantity} ${draft.unit}` : quantity
}

function AmountField({ index, item, busy, onChange }: { index: number; item: EditableIngredient; busy: boolean; onChange: (item: EditableIngredient, value: string) => void }) {
  const [draft, setDraft] = useState(() => parseAmount(item.amount))

  function emit(next: AmountDraft) {
    setDraft(next)
    onChange(item, composeAmount(next))
  }

  if (draft.mode === 'free') {
    return <input className="amount-free" aria-label={`Ingredient ${index + 1} amount`} value={draft.text} maxLength={100} placeholder="amount"
      disabled={busy} onChange={event => {
        const text = event.target.value
        if (!text.trim()) {
          emit({ mode: 'picker', quantity: '', unit: '', text: '' })
          return
        }
        emit({ mode: 'free', quantity: '', unit: '', text })
      }} />
  }

  const unitValue = canonicalUnit(draft.unit)
  const select = <select className="unit" aria-label={`Ingredient ${index + 1} unit`} value={unitValue} disabled={busy}
    onChange={event => emit({ ...draft, unit: event.target.value, quantity: event.target.value === 'to taste' ? '' : draft.quantity })}>
    {UNIT_OPTIONS.map(option => <option key={option || 'none'} value={option}>{option || '—'}</option>)}
  </select>

  if (unitValue === 'to taste') {
    return <span className="amount-control single">{select}</span>
  }

  return <span className="amount-control">
    <input className="qty" aria-label={`Ingredient ${index + 1} quantity`} inputMode="decimal" value={draft.quantity} maxLength={24} placeholder="1"
      disabled={busy} onChange={event => emit({ ...draft, quantity: event.target.value.replace(ALLOWED_AMOUNT, '') })} />
    {select}
  </span>
}

function SourcePanel({ original, warnings }: { original: Document | null; warnings: string[] }) {
  if (!original) {
    return <p className="fine">The source recipe is unavailable.</p>
  }

  return <>
    <h2>Source</h2>
    {warnings.length > 0 && <>
      <h3>Needs review</h3>
      <ul className="source-warnings">{warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul>
    </>}
    <h3>Source method</h3>
    <ol>{original.steps.map((step, index) => <li key={index}>{step.text}</li>)}</ol>
    {original.sourceCaption && <>
      <h3>Public caption</h3>
      <p className="fine caption">{original.sourceCaption}</p>
    </>}
    {original.frames.length > 0 && <>
      <h3>Frames</h3>
      <div className="frames">{original.frames.slice(0, 6).map(frame => <img key={frame} src={frame} alt="" loading="lazy" />)}</div>
    </>}
    {original.video && <p><a href={original.video} target="_blank" rel="noreferrer">Play the saved video</a></p>}
  </>
}

function reorder<T>(items: T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) {
    return items
  }

  const result = [...items]
  const [moved] = result.splice(from, 1)
  result.splice(to, 0, moved)
  return result
}

export function RecipeEditor({ title, setTitle, ingredients, setIngredients, steps, setSteps, tags, setTags,
  busy, changes, original, warnings, error, notice, setNotice, newRowKey, focusRow, onSubmit, onDiscard }: RecipeEditorProps) {
  const [dragKind, setDragKind] = useState<RowKind | null>(null)
  const [dragIndex, setDragIndex] = useState<number | null>(null)
  const [dropIndex, setDropIndex] = useState<number | null>(null)
  const [tagDraft, setTagDraft] = useState('')
  const [tagOptions, setTagOptions] = useState<string[]>([])
  const [wideScreen, setWideScreen] = useState(() => window.matchMedia('(min-width: 1100px)').matches)
  const tagList = tags.split(',').map(tag => tag.trim()).filter(Boolean)

  useEffect(() => {
    const query = window.matchMedia('(min-width: 1100px)')
    const update = () => setWideScreen(query.matches)
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [])
  useEffect(() => {
    let active = true
    api<string[]>('/api/collection/tags?popular=true')
      .then(items => { if (active) setTagOptions(items) })
      .catch(() => { if (active) setTagOptions([]) })
    return () => { active = false }
  }, [])

  function setAmount(item: EditableIngredient, value: string) {
    setIngredients(old => old.map(row => row.key === item.key ? { ...row, amount: value } : row))
  }
  function addIngredient() {
    if (ingredients.length >= 100) {
      return
    }
    const key = newRowKey()
    focusRow(`ingredient-${key}`)
    setIngredients(old => [...old, { key, name: '', amount: '' }])
    setNotice('Ingredient added.')
  }
  function insertIngredient(index: number) {
    if (ingredients.length >= 100) {
      return
    }
    const key = newRowKey()
    focusRow(`ingredient-${key}`)
    setIngredients(old => [...old.slice(0, index + 1), { key, name: '', amount: '' }, ...old.slice(index + 1)])
    setNotice(`Ingredient added at position ${index + 2}.`)
  }
  function addStep() {
    if (steps.length >= 100) {
      return
    }
    const key = newRowKey()
    focusRow(`step-${key}`)
    setSteps(old => [...old, { key, text: '', originalText: '', timestampSeconds: null }])
    setNotice('Step added.')
  }
  function removeIngredient(index: number) {
    const neighbour = ingredients[index + 1] ?? ingredients[index - 1]
    if (neighbour) {
      focusRow(`ingredient-${neighbour.key}`)
    }
    setIngredients(old => old.filter((_, position) => position !== index))
    setNotice(`Ingredient ${index + 1} removed.`)
  }
  function removeStep(index: number) {
    const neighbour = steps[index + 1] ?? steps[index - 1]
    if (neighbour) {
      focusRow(`step-${neighbour.key}`)
    }
    setSteps(old => old.filter((_, position) => position !== index))
    setNotice(`Step ${index + 1} removed.`)
  }
  function reorderRows(kind: RowKind, from: number, to: number) {
    const length = kind === 'ingredient' ? ingredients.length : steps.length
    if (from === to || to < 0 || to >= length) {
      return
    }

    if (kind === 'ingredient') {
      const moved = ingredients[from]
      setIngredients(old => reorder(old, from, to))
      if (moved) {
        focusRow(`ingredient-${moved.key}`)
      }
      setNotice(`Ingredient moved to position ${to + 1}.`)
    } else {
      const moved = steps[from]
      setSteps(old => reorder(old, from, to))
      if (moved) {
        focusRow(`step-${moved.key}`)
      }
      setNotice(`Step moved to position ${to + 1}.`)
    }
  }
  function handleReorderKeys(kind: RowKind, index: number, event: KeyboardEvent<HTMLElement>) {
    if (!event.altKey) {
      return
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault()
      reorderRows(kind, index, index - 1)
    }
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      reorderRows(kind, index, index + 1)
    }
  }
  function handleDragStart(kind: RowKind, index: number, event: DragEvent<HTMLElement>) {
    event.dataTransfer.setData('text/plain', '')
    event.dataTransfer.effectAllowed = 'move'
    setDragKind(kind)
    setDragIndex(index)
  }
  function handleDragOver(kind: RowKind, index: number, event: DragEvent<HTMLElement>) {
    if (dragKind !== kind) {
      return
    }
    event.preventDefault()
    setDropIndex(index)
  }
  function handleDrop(kind: RowKind, index: number, event: DragEvent<HTMLElement>) {
    if (dragKind !== kind || dragIndex === null) {
      return
    }
    event.preventDefault()
    reorderRows(kind, dragIndex, index)
    setDragKind(null); setDragIndex(null); setDropIndex(null)
  }
  function endDrag() {
    setDragKind(null); setDragIndex(null); setDropIndex(null)
  }
  function commitTag() {
    const values = tagDraft.split(',').map(value => value.trim()).filter(Boolean)
    if (values.length === 0) {
      setTagDraft('')
      return
    }

    const next = [...tagList]
    const added: string[] = []
    for (const value of values) {
      if (!next.some(tag => tag.toLowerCase() === value.toLowerCase())) {
        next.push(value)
        added.push(value)
      }
    }
    setTagDraft('')
    if (added.length === 0) {
      return
    }
    setTags(next.join(', '))
    setNotice(added.length === 1 ? `Tag ${added[0]} added.` : `${added.length} tags added.`)
  }
  function removeTag(tag: string) {
    setTags(tagList.filter(item => item !== tag).join(', '))
    setNotice(`Tag ${tag} removed.`)
  }

  const rowClass = (kind: RowKind, index: number) =>
    `edit-line${dragKind === kind && dragIndex === index ? ' dragging' : ''}${dragKind === kind && dropIndex === index ? ' drop-target' : ''}`
  const stepClass = (index: number) =>
    `step-line${dragKind === 'step' && dragIndex === index ? ' dragging' : ''}${dragKind === 'step' && dropIndex === index ? ' drop-target' : ''}`

  const source = <SourcePanel original={original} warnings={warnings} />
  const moveButtons = (kind: RowKind, index: number, total: number) => <span className="touch-move">
    <button type="button" disabled={busy || index === 0} aria-label={`Move ${kind} ${index + 1} up`} onClick={() => reorderRows(kind, index, index - 1)}>↑</button>
    <button type="button" disabled={busy || index === total - 1} aria-label={`Move ${kind} ${index + 1} down`} onClick={() => reorderRows(kind, index, index + 1)}>↓</button>
  </span>

  return <div className="editor-layout">
    <div className="editor-main">
      <form onSubmit={onSubmit} className="editor" onKeyDown={event => {
        const target = event.target as HTMLElement
        if (event.key === 'Enter' && !target.closest('.tag-editor') && (target.tagName === 'INPUT' || target.tagName === 'SELECT')) {
          event.preventDefault()
        }
      }}>
        <div className="editor-bar">
          <div className="editor-bar-copy">
            <h1>Correct recipe</h1>
            <span role="status">{changes === 0 ? 'No changes yet' : `${changes} ${changes === 1 ? 'change' : 'changes'}`}</span>
          </div>
          <div className="actions">
            <Button variant="secondary" disabled={busy} onClick={onDiscard}>{changes === 0 ? 'Back to recipe' : 'Discard'}</Button>
            <Button type="submit" disabled={busy || changes === 0}>{busy ? 'Saving…' : 'Save changes'}</Button>
          </div>
          {error && <p className="editor-error" role="alert">{error}</p>}
        </div>
        {!wideScreen && <details className="editor-source-mobile"><summary>Show the source</summary>{source}</details>}
        <p className="fine">Change only what needs correcting. Source information and uncertainties are kept.</p>
        <label>Title<input value={title} onChange={event => setTitle(event.target.value)} maxLength={200} disabled={busy} required /></label>

        <fieldset className="edit-group"><legend>Ingredients</legend><p className="fine">Leave an amount blank if it is unknown. Drag a row to reorder, or press Alt+↑ / Alt+↓ inside it.</p>
          <ol className="edit-list">
            {ingredients.map((item, index) => <li key={item.key} className={rowClass('ingredient', index)}
              onKeyDown={event => handleReorderKeys('ingredient', index, event)}
              onDragOver={event => handleDragOver('ingredient', index, event)}
              onDrop={event => handleDrop('ingredient', index, event)}>
              <span className="row-lead">
                <span className="drag" draggable title="Drag to reorder" onDragStart={event => handleDragStart('ingredient', index, event)} onDragEnd={endDrag}>⠿</span>
                {moveButtons('ingredient', index, ingredients.length)}
              </span>
              <input className="name" id={`ingredient-${item.key}`} aria-label={`Ingredient ${index + 1} name`} value={item.name} maxLength={150}
                placeholder="ingredient" disabled={busy} required onChange={event => setIngredients(old => old.map(row => row.key === item.key ? { ...row, name: event.target.value } : row))}
                onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); insertIngredient(index) } }} />
              <AmountField index={index} item={item} busy={busy} onChange={setAmount} />
              <button type="button" className="row-remove" disabled={busy || ingredients.length === 1} aria-label={`Remove ingredient ${index + 1}`} onClick={() => removeIngredient(index)}>×</button>
            </li>)}
          </ol>
          <button type="button" className="add-row" disabled={busy || ingredients.length >= 100} onClick={addIngredient}>+ Add ingredient</button>
        </fieldset>

        <fieldset className="edit-group"><legend>Method</legend><p className="fine">Describe one cooking action per step. Editing a step clears its video timestamp.</p>
          <ol className="step-list">
            {steps.map((item, index) => <li key={item.key} className={stepClass(index)}
              onKeyDown={event => handleReorderKeys('step', index, event)}
              onDragOver={event => handleDragOver('step', index, event)}
              onDrop={event => handleDrop('step', index, event)}>
              <span className="row-lead">
                <span className="drag" draggable title="Drag to reorder" onDragStart={event => handleDragStart('step', index, event)} onDragEnd={endDrag}>⠿</span>
                {moveButtons('step', index, steps.length)}
              </span>
              <textarea id={`step-${item.key}`} aria-label={`Step ${index + 1}`} value={item.text} maxLength={2000} rows={2} minLength={12}
                placeholder="Describe one cooking action" disabled={busy} required onChange={event => setSteps(old => old.map(row => row.key === item.key ? { ...row, text: event.target.value } : row))} />
              <button type="button" className="row-remove" disabled={busy || steps.length === 1} aria-label={`Remove step ${index + 1}`} onClick={() => removeStep(index)}>×</button>
            </li>)}
          </ol>
          <button type="button" className="add-row" disabled={busy || steps.length >= 100} onClick={addStep}>+ Add step</button>
        </fieldset>

        <fieldset className="edit-group"><legend>Tags</legend><p className="fine">Press Enter or comma to add a tag. Tags help you find the recipe later.</p>
          <div className="tag-editor">
            {tagList.map(tag => <span className="tag-chip" key={tag}>{tag}<button type="button" disabled={busy} aria-label={`Remove tag ${tag}`} onClick={() => removeTag(tag)}>×</button></span>)}
            <input list="editor-tag-options" aria-label="Add a tag" value={tagDraft} disabled={busy} placeholder={tagList.length === 0 ? 'dinner, spicy…' : 'add a tag'}
              onChange={event => setTagDraft(event.target.value)}
              onKeyDown={event => {
                if (event.key === 'Enter' || event.key === ',') {
                  event.preventDefault(); event.stopPropagation(); commitTag()
                }
                if (event.key === 'Backspace' && !tagDraft && tagList.length > 0) {
                  removeTag(tagList[tagList.length - 1])
                }
              }} />
            <datalist id="editor-tag-options">{tagOptions.filter(option => !tagList.some(tag => tag.toLowerCase() === option.toLowerCase())).map(option => <option key={option} value={option} />)}</datalist>
          </div>
        </fieldset>
        <p className="sr-only" role="status">{notice}</p>
      </form>
    </div>
    {wideScreen && <aside className="editor-source" aria-label="Source recipe">{source}</aside>}
  </div>
}
