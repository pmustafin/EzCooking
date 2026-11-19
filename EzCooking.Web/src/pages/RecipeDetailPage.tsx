import type { RefObject } from 'react'
import type { Document, Recipe, Translation } from '../types'
import { Button } from '../components/Button'
import { Pill } from '../components/Pill'
import { VideoPlayer } from '../components/VideoPlayer'

type RecipeDetailPageProps = {
  recipe: Recipe
  data: Document
  largeScreen: boolean
  videoError: boolean
  busy: boolean
  translation: Translation | null
  translationLoading: boolean
  translationError: string
  translationBusy: boolean
  confirmTranslation: boolean
  showRussian: boolean
  originalButtonRef: RefObject<HTMLButtonElement | null>
  russianButtonRef: RefObject<HTMLButtonElement | null>
  onVideoError: () => void
  onShowOriginal: () => void
  onSelectRussian: () => void
  onCheckAgain: () => void
  onRetryTranslation: () => void
  onCancelConfirm: () => void
  onRequestTranslation: () => void
  onEdit: () => void
  onDelete: () => void
}

export function RecipeDetailPage({ recipe, data, largeScreen, videoError, busy, translation, translationLoading,
  translationError, translationBusy, confirmTranslation, showRussian, originalButtonRef, russianButtonRef,
  onVideoError, onShowOriginal, onSelectRussian, onCheckAgain, onRetryTranslation, onCancelConfirm,
  onRequestTranslation, onEdit, onDelete }: RecipeDetailPageProps) {
  const videoPlayer = <VideoPlayer video={data.video} cover={data.cover} title={data.title} sourceUrl={recipe.sourceUrl} error={videoError} onError={onVideoError} />

  return <>
    <div className="detail-head"><div><h1>{data.title}</h1>{data.tags.length > 0 && <p className="detail-tags">{data.tags.map(tag => <Pill key={tag}>{tag}</Pill>)}</p>}
      <div className="translation">
        <div className="language-control" role="group" aria-label="Recipe language">
          <button ref={originalButtonRef} type="button" aria-pressed={!showRussian} onClick={onShowOriginal}>Original</button>
          <button ref={russianButtonRef} type="button" lang="ru" aria-pressed={showRussian} aria-expanded={translation?.status === 'complete' ? undefined : confirmTranslation} aria-controls={confirmTranslation ? 'translation-confirm' : undefined} disabled={translationLoading || translationBusy || translation?.status === 'queued' || translation?.status === 'processing' || (!!translationError && !confirmTranslation)}
            onClick={onSelectRussian}>Русский</button>
        </div>
        {translationLoading ? <p className="translation-status" role="status">Checking Russian translation…</p> :
          translation?.status === 'queued' || translation?.status === 'processing' ? <p className="translation-status" role="status">Russian translation {translation.status === 'queued' ? 'queued' : 'in progress'}. You can leave this page.</p> :
          showRussian ? <p className="translation-status" role="status">AI translation. Check the original before cooking.</p> :
          translation?.status === 'complete' && translation.document ? <p className="translation-status" role="status">Russian translation available.</p> :
          !translation && !translationError && !confirmTranslation ? <p className="translation-status">Russian translation not yet available.</p> : null}
        {translationError && <div className="translation-message"><p role="alert">{translationError}</p>
          {!confirmTranslation && <Button variant="secondary" onClick={onCheckAgain}>Check again</Button>}</div>}
        {translation?.status === 'failed' && !confirmTranslation && !translationError && <div className="translation-message"><p role="alert">Russian translation unavailable: {translation.error ?? 'Translation failed.'}</p><Button variant="secondary" onClick={onRetryTranslation}>Retry translation</Button></div>}
        {confirmTranslation && <div className="translation-confirm" id="translation-confirm">
          <p><strong>Russian translation is not available yet.</strong></p>
          <p>Send this recipe’s text to OpenRouter and its selected provider? This uses a separate free-model request.</p>
          <div className="actions"><Button disabled={translationBusy} onClick={onRequestTranslation}>{translationBusy ? 'Sending…' : translationError ? 'Retry sending' : 'Translate and send'}</Button><Button variant="secondary" disabled={translationBusy} onClick={onCancelConfirm}>Cancel</Button></div>
        </div>}
      </div>
    </div><div className="actions"><Button variant="secondary" disabled={busy} onClick={onEdit}>Correct recipe</Button><Button variant="secondary" disabled={busy} onClick={onDelete}>Delete recipe</Button></div></div>
    <div className="recipe-intro">{largeScreen && data.video ? videoPlayer : <img className="cover" src={data.cover} alt={`Frame from the video for ${data.title}`} />}<div className="recipe-context"><h2>About this recipe</h2>
      <p className="evidence">{data.recipeSource === 'caption' ? 'The method comes from the public caption. Ingredients and amounts were organized by AI and may be incomplete or misinterpreted. Check the original before cooking.' : data.recipeSource === 'edited' ? 'This recipe was edited. Check it against the original before cooking.' : 'The photos are video frames. Ingredients and actions are AI inferences, not independently confirmed source details. Check the original before cooking.'}</p>
      {data.attribution && <p className="source-attribution">Source: {data.attribution}</p>}
      {recipe.sourceUrl && <a href={recipe.sourceUrl} target="_blank" rel="noreferrer">Open the original Instagram reel</a>}
      {recipe.notes && recipe.notes.length > 0 && <ul className="source-notes">{recipe.notes.map((note, index) => <li key={index}>{note}</li>)}</ul>}
    </div></div>
    {recipe.warnings.length > 0 && <div className="error recipe-warning"><div><strong className="recipe-warning-title">Needs review</strong><ul>{recipe.warnings.map((w, n) => <li key={n}>{w}</li>)}</ul><p>Check the original or correct the recipe.</p></div></div>}
    <div className="recipe-body"><section className="ingredients-section"><h2>Ingredients</h2><p className="fine ingredient-note">{data.recipeSource === 'caption' ? 'Amounts were extracted from the caption; verify each ingredient and amount against the original.' : data.recipeSource === 'edited' ? 'Amounts may include user edits and unverified AI suggestions.' : 'Amounts marked ~ are estimates, not the creator’s exact quantities.'}</p><ul className="ingredient-list">{data.ingredients.map((i, n) => <li key={n}><span>{i.name}</span><span>{i.estimatedAmount ? data.recipeSource === 'caption' || data.recipeSource === 'edited' ? i.estimatedAmount : `~ ${i.estimatedAmount}` : 'Amount unknown'}</span></li>)}</ul></section>
      <section className="method-section"><h2>Method</h2><ol className="method">{data.steps.map((s, n) => <li key={n}>{s.text}</li>)}</ol><div className="uncertainty"><h2>Before you cook</h2>{data.uncertainties.length > 0 && <ul>{data.uncertainties.map((u, n) => <li key={n}>{u}</li>)}</ul>}<p>Check cooking temperatures and food handling against a trusted source.</p></div></section></div>
    <section className="recipe-source"><h2>{largeScreen && data.video ? 'Original source' : 'Original video'}</h2><div className="source-layout">{(!largeScreen || !data.video) && videoPlayer}<div><p>Review the video alongside the recipe.</p>{recipe.sourceUrl && <p><a href={recipe.sourceUrl} target="_blank" rel="noreferrer">Open the original Instagram reel</a></p>}{data.attribution && <p className="fine">Source: {data.attribution}</p>}</div></div>
      {data.sourceCaption && <p className="fine caption"><strong>Public caption:</strong> {data.sourceCaption}</p>}
      <details className="analysis-details"><summary>More frames and analysis details</summary><div className="frames">{data.frames.map(f => <img key={f} src={f} alt="Frame from the source video" loading="lazy" />)}</div><p className="fine">Vision model: {data.model}. {data.audioModel && `Audio model: ${data.audioModel}. Audio reported cost: ${data.audioCost == null ? 'not provided' : `$${data.audioCost}`}. `}Total reported cost: {data.cost == null ? 'not provided' : `$${data.cost}`}.</p></details>
    </section>
  </>
}
