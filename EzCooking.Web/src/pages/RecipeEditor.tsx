import type { Dispatch, FormEvent, SetStateAction } from 'react'
import type { EditableIngredient, EditableStep } from '../types'

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
  notice: string
  setNotice: (value: string) => void
  newRowKey: () => number
  focusRow: (id: string) => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  onCancel: () => void
}

function moveRow<T>(items: T[], index: number, direction: -1 | 1): T[] {
  const target = index + direction
  if (target < 0 || target >= items.length) {
    return items
  }

  const result = [...items]
  ;[result[index], result[target]] = [result[target], result[index]]
  return result
}

export function RecipeEditor({ title, setTitle, ingredients, setIngredients, steps, setSteps, tags, setTags,
  busy, notice, setNotice, newRowKey, focusRow, onSubmit, onCancel }: RecipeEditorProps) {
  return <form onSubmit={onSubmit} className="editor"><h1>Correct recipe</h1>
    <p className="fine">Change only what needs correcting. Source information and uncertainties are kept.</p>
    <label>Title<input value={title} onChange={e => setTitle(e.target.value)} maxLength={200} disabled={busy} required /></label>
    <fieldset className="edit-group"><legend>Ingredients</legend><p className="fine">Leave an amount blank if it is unknown.</p>
      {ingredients.map((item, index) => <div className="edit-row" key={item.key}>
        <div className="edit-row-heading"><strong>Ingredient {index + 1}</strong></div>
        <div className="ingredient-fields"><label>Name<input id={`ingredient-${item.key}`} aria-label={`Ingredient ${index + 1} name`} value={item.name} maxLength={150} disabled={busy} required onChange={e => setIngredients(old => old.map(row => row.key === item.key ? { ...row, name: e.target.value } : row))} /></label>
          <label>Amount <small>(optional)</small><input aria-label={`Ingredient ${index + 1} amount`} value={item.amount} maxLength={100} disabled={busy} onChange={e => setIngredients(old => old.map(row => row.key === item.key ? { ...row, amount: e.target.value } : row))} /></label></div>
        <div className="actions row-actions">
          <button type="button" className="secondary" disabled={busy || index === 0} aria-label={`Move ingredient ${index + 1} up`} onClick={() => { focusRow(`ingredient-${item.key}`); setIngredients(old => moveRow(old, index, -1)); setNotice(`Ingredient moved to position ${index}.`) }}>Move up</button>
          <button type="button" className="secondary" disabled={busy || index === ingredients.length - 1} aria-label={`Move ingredient ${index + 1} down`} onClick={() => { focusRow(`ingredient-${item.key}`); setIngredients(old => moveRow(old, index, 1)); setNotice(`Ingredient moved to position ${index + 2}.`) }}>Move down</button>
          <button type="button" className="secondary" disabled={busy || ingredients.length === 1} aria-label={`Remove ingredient ${index + 1}`} onClick={() => { focusRow(`ingredient-${ingredients[index + 1]?.key ?? ingredients[index - 1].key}`); setIngredients(old => old.filter(row => row.key !== item.key)); setNotice(`Ingredient ${index + 1} removed.`) }}>Remove</button>
        </div>
      </div>)}
      <button type="button" className="secondary" disabled={busy || ingredients.length >= 100} onClick={() => { const key = newRowKey(); focusRow(`ingredient-${key}`); setIngredients(old => [...old, { key, name: '', amount: '' }]); setNotice('Ingredient added.') }}>Add ingredient</button>
    </fieldset>
    <fieldset className="edit-group"><legend>Instructions</legend><p className="fine">Describe one cooking action per step. Editing a step clears its video timestamp.</p>
      {steps.map((item, index) => <div className="edit-row" key={item.key}>
        <label>Step {index + 1}<textarea id={`step-${item.key}`} aria-label={`Step ${index + 1}`} value={item.text} maxLength={2000} rows={3} disabled={busy} required onChange={e => setSteps(old => old.map(row => row.key === item.key ? { ...row, text: e.target.value } : row))} /></label>
        <div className="actions row-actions">
          <button type="button" className="secondary" disabled={busy || index === 0} aria-label={`Move step ${index + 1} up`} onClick={() => { focusRow(`step-${item.key}`); setSteps(old => moveRow(old, index, -1)); setNotice(`Step moved to position ${index}.`) }}>Move up</button>
          <button type="button" className="secondary" disabled={busy || index === steps.length - 1} aria-label={`Move step ${index + 1} down`} onClick={() => { focusRow(`step-${item.key}`); setSteps(old => moveRow(old, index, 1)); setNotice(`Step moved to position ${index + 2}.`) }}>Move down</button>
          <button type="button" className="secondary" disabled={busy || steps.length === 1} aria-label={`Remove step ${index + 1}`} onClick={() => { focusRow(`step-${steps[index + 1]?.key ?? steps[index - 1].key}`); setSteps(old => old.filter(row => row.key !== item.key)); setNotice(`Step ${index + 1} removed.`) }}>Remove</button>
        </div>
      </div>)}
      <button type="button" className="secondary" disabled={busy || steps.length >= 100} onClick={() => { const key = newRowKey(); focusRow(`step-${key}`); setSteps(old => [...old, { key, text: '', originalText: '', timestampSeconds: null }]); setNotice('Step added.') }}>Add step</button>
    </fieldset>
    <p className="sr-only" role="status">{notice}</p>
    <label>Tags, separated by commas<input value={tags} onChange={e => setTags(e.target.value)} disabled={busy} /></label>
    <div className="actions"><button disabled={busy}>Save changes</button><button type="button" className="secondary" disabled={busy} onClick={onCancel}>Cancel</button></div></form>
}
