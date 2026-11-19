import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'
import { api, json, message } from './api'
import { documentOf } from './recipe'
import { routeFromHash } from './routing'
import { CollectionPage } from './pages/CollectionPage'
import { AddPage } from './pages/AddPage'
import { ProcessingPage } from './pages/ProcessingPage'
import { RecipeDetailPage } from './pages/RecipeDetailPage'
import { RecipeEditor } from './pages/RecipeEditor'
import type { Document, EditableIngredient, EditableStep, Page, PollState, Recipe, Summary, Translation } from './types'

type EditorShape = { title: string; ingredients: string[][]; steps: string[]; tags: string }

function countEditorChanges(baseline: EditorShape, current: EditorShape): number {
  let changes = baseline.title !== current.title ? 1 : 0
  if (baseline.tags !== current.tags) {
    changes++
  }

  const ingredientRows = Math.max(baseline.ingredients.length, current.ingredients.length)
  for (let index = 0; index < ingredientRows; index++) {
    if (JSON.stringify(baseline.ingredients[index] ?? null) !== JSON.stringify(current.ingredients[index] ?? null)) {
      changes++
    }
  }

  const stepRows = Math.max(baseline.steps.length, current.steps.length)
  for (let index = 0; index < stepRows; index++) {
    if ((baseline.steps[index] ?? null) !== (current.steps[index] ?? null)) {
      changes++
    }
  }

  return changes
}

function App() {
  const [route, setRoute] = useState(routeFromHash)
  const [search, setSearch] = useState('')
  const [tag, setTag] = useState('')
  const [tagInput, setTagInput] = useState('')
  const [tagSuggestions, setTagSuggestions] = useState<string[]>([])
  const [tagError, setTagError] = useState('')
  const [tagsLoading, setTagsLoading] = useState(true)
  const [collectionFailed, setCollectionFailed] = useState(false)
  const [collection, setCollection] = useState<Page<Summary>>({ items: [], hasMore: false })
  const [jobs, setJobs] = useState<Page<Recipe>>({ items: [], hasMore: false })
  const [pollStates, setPollStates] = useState<Record<string, PollState>>({})
  const [showDismissed, setShowDismissed] = useState(false)
  const [retryId, setRetryId] = useState<string | null>(null)
  const [selected, setSelected] = useState<Recipe | null>(null)
  const [largeScreen, setLargeScreen] = useState(() => window.matchMedia('(min-width: 751px)').matches)
  const [videoError, setVideoError] = useState(false)
  const [url, setUrl] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const [consent, setConsent] = useState(false)
  const [audioConsent, setAudioConsent] = useState(false)
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [editing, setEditing] = useState(false)
  const [translation, setTranslation] = useState<Translation | null>(null)
  const [translationLoading, setTranslationLoading] = useState(true)
  const [translationError, setTranslationError] = useState('')
  const [confirmTranslation, setConfirmTranslation] = useState(false)
  const [translationBusy, setTranslationBusy] = useState(false)
  const [showRussian, setShowRussian] = useState(false)
  const [translationRefresh, setTranslationRefresh] = useState(0)
  const showWhenReady = useRef(false)
  const previousHash = useRef(window.location.hash)
  const originalLanguageButton = useRef<HTMLButtonElement>(null)
  const russianLanguageButton = useRef<HTMLButtonElement>(null)
  const [editVersion, setEditVersion] = useState(0)
  const [title, setTitle] = useState('')
  const [ingredients, setIngredients] = useState<EditableIngredient[]>([])
  const [steps, setSteps] = useState<EditableStep[]>([])
  const nextRowKey = useRef(0)
  const editorFocus = useRef<string | null>(null)
  const [editorNotice, setEditorNotice] = useState('')
  const [editorBaseline, setEditorBaseline] = useState<EditorShape | null>(null)
  const [tags, setTags] = useState('')
  const original = selected ? documentOf(selected) : null
  const data = showRussian && translation && translation.version === selected?.version && translation.document ? translation.document : original
  const translationRecipeId = selected?.id
  const translationRecipeVersion = selected?.version
  const translationRecipeStatus = selected?.status
  const activeJobIds = jobs.items.filter(j => ['staging', 'queued', 'processing'].includes(j.status)).map(j => j.id).join(',')
  const editorShape: EditorShape = {
    title: title.trim(),
    ingredients: ingredients.map(item => [item.name.trim(), item.amount.trim()]),
    steps: steps.map(item => item.text.trim()),
    tags: tags.split(',').map(tag => tag.trim()).filter(Boolean).join(', ')
  }
  const editorChanges = editorBaseline ? countEditorChanges(editorBaseline, editorShape) : 0

  useEffect(() => {
    if (editorFocus.current) {
      document.getElementById(editorFocus.current)?.focus()
      editorFocus.current = null
    }
  }, [ingredients, steps])
  useEffect(() => {
    const query = window.matchMedia('(min-width: 751px)')
    const update = () => setLargeScreen(query.matches)
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [])
  useEffect(() => {
    const onHash = () => {
      if (editing && editorChanges > 0 && !window.confirm('Discard your unsaved corrections?')) {
        history.replaceState(null, '', previousHash.current)
        return
      }
      previousHash.current = window.location.hash
      setRoute(routeFromHash()); setError(''); setLoading(true); setSelected(null); setVideoError(false); setEditing(false); setEditorBaseline(null); setRetryId(null); setConsent(false); setAudioConsent(false); setTranslation(null); setTranslationLoading(true); setTranslationError(''); setConfirmTranslation(false); setShowRussian(false); showWhenReady.current = false
    }
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [editing, editorChanges])
  useEffect(() => {
    if (route.page !== 'collection') return
    let active = true
    const timer = window.setTimeout(() => {
      api<Page<Summary>>(`/api/collection?search=${encodeURIComponent(search)}&tag=${encodeURIComponent(tag)}&offset=0`)
        .then(page => { if (active) { setCollection(page); setCollectionFailed(false); setError('') } })
        .catch(e => { if (active) { setCollectionFailed(true); setError(message(e)) } })
        .finally(() => { if (active) setLoading(false) })
    }, 250)
    return () => { active = false; window.clearTimeout(timer) }
  }, [route.page, search, tag])
  useEffect(() => {
    if (route.page !== 'collection') return
    let active = true
    const timer = window.setTimeout(() => {
      api<string[]>(`/api/collection/tags?popular=true&search=${encodeURIComponent(tagInput)}`)
        .then(items => { if (active) { setTagSuggestions(items); setTagError('') } })
        .catch(e => { if (active) { setTagSuggestions([]); setTagError(`Could not load tag suggestions: ${message(e)}`) } })
        .finally(() => { if (active) setTagsLoading(false) })
    }, 250)
    return () => { active = false; window.clearTimeout(timer) }
  }, [route.page, tagInput])
  useEffect(() => {
    if (route.page !== 'processing') return
    let active = true
    api<Page<Recipe>>(`/api/processing?dismissed=${showDismissed}&offset=0`)
      .then(page => { if (active) { setJobs(page); setError('') } })
      .catch(e => { if (active) setError(message(e)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [route.page, showDismissed])
  useEffect(() => {
    if (route.page !== 'processing' || showDismissed) return
    let active = true
    async function checkForNewJobs() {
      if (document.hidden || busy) return
      try {
        const page = await api<Page<Recipe>>('/api/processing?dismissed=false&offset=0')
        if (!active) return
        setJobs(old => {
          const known = new Set(old.items.map(item => item.id))
          const fresh = page.items.filter(item => !known.has(item.id))
          return fresh.length === 0 ? old : { items: [...fresh, ...old.items], hasMore: old.hasMore }
        })
      } catch {
        // Keep the current list; the next tick tries again.
      }
    }
    const timer = window.setInterval(checkForNewJobs, 5000)
    return () => { active = false; window.clearInterval(timer) }
  }, [route.page, showDismissed, busy])
  useEffect(() => {
    if (route.page !== 'processing' || showDismissed || !activeJobIds) return
    let active = true
    let timer: number
    async function poll() {
      const ids = activeJobIds.split(',')
      const results = await Promise.allSettled(ids.map(id => api<Recipe>(`/api/recipes/${id}`, { signal: AbortSignal.timeout(10000) })))
      if (!active) return
      const checkedAt = Date.now()
      setJobs(old => ({
        ...old,
        items: old.items.map(item => {
          const result = results[ids.indexOf(item.id)]
          return result?.status === 'fulfilled' ? result.value : item
        })
      }))
      setPollStates(old => {
        const next = { ...old }
        ids.forEach((id, i) => {
          next[id] = results[i].status === 'fulfilled'
            ? { checkedAt, unavailable: false }
            : { checkedAt: old[id]?.checkedAt ?? null, unavailable: true }
        })
        return next
      })
      timer = window.setTimeout(poll, 3000)
    }
    void poll()
    return () => { active = false; window.clearTimeout(timer) }
  }, [route.page, showDismissed, activeJobIds])
  useEffect(() => {
    if (route.page !== 'detail' || !route.id) return
    let active = true
    api<Recipe>(`/api/recipes/${route.id}`)
      .then(recipe => { if (active) setSelected(recipe) })
      .catch(e => { if (active) setError(message(e)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [route.page, route.id])
  useEffect(() => {
    if (route.page !== 'detail' || !translationRecipeId || translationRecipeStatus !== 'complete') return
    let active = true
    let timer: number | undefined
    async function checkTranslation() {
      try {
        const result = await api<Translation | undefined>(`/api/recipes/${translationRecipeId}/russian-translation`)
        if (!active) return
        setTranslation(result ?? null); setTranslationError('')
        if (result?.status === 'complete' && result.document && showWhenReady.current) {
          setShowRussian(true); showWhenReady.current = false
        }
        if (result?.status === 'failed') showWhenReady.current = false
        if (result && (result.status === 'queued' || result.status === 'processing')) timer = window.setTimeout(checkTranslation, 2000)
      } catch (e) {
        if (active) setTranslationError(`Could not check translation: ${message(e)}`)
      } finally {
        if (active) setTranslationLoading(false)
      }
    }
    void checkTranslation()
    return () => { active = false; if (timer !== undefined) window.clearTimeout(timer) }
  }, [route.page, translationRecipeId, translationRecipeVersion, translationRecipeStatus, translationRefresh])

  useEffect(() => {
    if (!editing || editorChanges === 0) return
    const onBeforeUnload = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = '' }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [editing, editorChanges])

  async function requestTranslation() {
    if (!selected || !confirmTranslation || translationBusy) return
    const id = selected.id
    setTranslationBusy(true); setTranslationError('')
    try {
      await api(`/api/recipes/${id}/russian-translation`, json({ version: selected.version, consent: true }))
      if (routeFromHash().id === id) {
        showWhenReady.current = true
        setConfirmTranslation(false); setTranslationLoading(true); setTranslationRefresh(value => value + 1)
        originalLanguageButton.current?.focus()
      }
    } catch (e) { if (routeFromHash().id === id) setTranslationError(message(e)) }
    finally { setTranslationBusy(false) }
  }

  async function moreCollection() {
    setBusy(true)
    try {
      const page = await api<Page<Summary>>(`/api/collection?search=${encodeURIComponent(search)}&tag=${encodeURIComponent(tag)}&offset=${collection.items.length}`)
      setCollection(old => ({ items: [...old.items, ...page.items], hasMore: page.hasMore }))
    } catch (e) { setError(message(e)) } finally { setBusy(false) }
  }
  async function reloadJobs() {
    setJobs(await api<Page<Recipe>>(`/api/processing?dismissed=${showDismissed}&offset=0`))
  }
  async function refreshJobs() {
    setBusy(true); setError('')
    try { await reloadJobs() }
    catch (e) { setError(`Could not refresh videos: ${message(e)}`) }
    finally { setBusy(false) }
  }
  async function moreJobs() {
    setBusy(true)
    try {
      const page = await api<Page<Recipe>>(`/api/processing?dismissed=${showDismissed}&offset=${jobs.items.length}`)
      setJobs(old => ({ items: [...old.items, ...page.items], hasMore: page.hasMore }))
    } catch (e) { setError(message(e)) } finally { setBusy(false) }
  }
  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError('')
    try {
      if (file) {
        const form = new FormData()
        form.append('video', file); form.append('consent', String(consent)); form.append('audioConsent', String(audioConsent)); form.append('url', url.trim())
        await api<Recipe>('/api/recipes/upload', { method: 'POST', body: form })
      } else await api<Recipe>('/api/recipes', json({ url, consent, audioConsent }))
      setUrl(''); setFile(null); setConsent(false); setAudioConsent(false); setShowDismissed(false)
      if (fileInput.current) fileInput.current.value = ''
      window.location.hash = '#/processing'
    } catch (e) { setError(message(e)) } finally { setBusy(false) }
  }
  async function retry(id: string) {
    if (!consent) { setError('Select consent before retrying this video.'); return }
    setBusy(true); setError('')
    try {
      await api(`/api/recipes/${id}/retry`, json({ consent, audioConsent }))
      setRetryId(null); setConsent(false); setAudioConsent(false); setShowDismissed(false)
      if (!showDismissed) await reloadJobs()
    } catch (e) { setError(message(e)) } finally { setBusy(false) }
  }
  async function dismiss(id: string, dismissed: boolean) {
    setBusy(true); setError('')
    try { await api(`/api/recipes/${id}/dismiss`, json({ dismissed })); setRetryId(null); setConsent(false); setAudioConsent(false); await reloadJobs() }
    catch (e) { setError(message(e)) } finally { setBusy(false) }
  }
  async function remove(id: string, isRecipe: boolean) {
    if (!window.confirm(`Permanently delete this ${isRecipe ? 'recipe' : 'failed video'} and its local video and photos? This cannot be undone.`)) return
    setBusy(true); setError('')
    try {
      await api(`/api/recipes/${id}`, { method: 'DELETE' })
      if (isRecipe) window.location.hash = '#/'
      else { setRetryId(null); await reloadJobs() }
    } catch (e) {
      const failure = message(e)
      try { if (!isRecipe) await reloadJobs(); setError(failure) }
      catch (reloadError) { setError(`${failure} Could not refresh jobs: ${message(reloadError)}`) }
    }
    finally { setBusy(false) }
  }
  function startEdit(document: Document) {
    const nextIngredients = document.ingredients.map(i => ({ key: newRowKey(), name: i.name, amount: i.estimatedAmount ?? '' }))
    const nextSteps = document.steps.map(s => ({ key: newRowKey(), text: s.text, originalText: s.text, timestampSeconds: s.timestampSeconds }))
    const nextTags = document.tags.join(', ')
    setEditVersion(selected?.version ?? 0)
    setTitle(document.title)
    setIngredients(nextIngredients)
    setSteps(nextSteps)
    setTags(nextTags)
    setEditorBaseline({
      title: document.title.trim(),
      ingredients: nextIngredients.map(i => [i.name.trim(), i.amount.trim()]),
      steps: nextSteps.map(s => s.text.trim()),
      tags: nextTags.split(',').map(tag => tag.trim()).filter(Boolean).join(', ')
    })
    setEditorNotice(''); setError(''); setEditing(true)
  }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selected || editorChanges === 0) return
    setBusy(true); setError('')
    try {
      const body = { version: editVersion, title, ingredients: ingredients.map(i => ({
        name: i.name.trim(), amount: i.amount.trim() || null, evidence: 'edited'
      })), steps: steps.map(s => ({ text: s.text.trim(),
        timestampSeconds: s.text.trim() === s.originalText.trim() ? s.timestampSeconds : null, evidence: 'edited' })),
      tags: data && tags === data.tags.join(', ') ? data.tags : tags.split(',').map(t => t.trim()).filter(Boolean) }
      const updated = await api<Recipe>(`/api/recipes/${selected.id}`, { ...json(body), method: 'PUT' })
      setTranslation(null); setTranslationLoading(true); setConfirmTranslation(false); setShowRussian(false); showWhenReady.current = false; setSelected(updated)
      setEditorBaseline(null)
      setEditing(false)
    } catch (e) {
      if (message(e) === 'Request failed (409).') {
        setError('This recipe changed elsewhere. Your corrections are still here; save again to overwrite the newer version.')
        try {
          setEditVersion((await api<Recipe>(`/api/recipes/${selected.id}`)).version)
        } catch {
          setError('This recipe changed elsewhere. Reload the page to see the newer version.')
        }
      } else {
        setError(message(e))
      }
    }
    finally { setBusy(false) }
  }

  function discardEdit() {
    if (editorChanges > 0 && !window.confirm('Discard your unsaved corrections?')) return
    setError(''); setEditorBaseline(null); setEditing(false)
  }
  function newRowKey() {
    return nextRowKey.current++
  }
  function focusRow(id: string) {
    editorFocus.current = id
  }
  function changeSearch(value: string) {
    setLoading(true); setSearch(value)
  }
  function selectTag(value: string) {
    if (value !== tag) { setLoading(true); setTag(value) }
  }
  function clearTag() {
    setLoading(true); setTag('')
    if (tagInput) { setTagsLoading(true); setTagSuggestions([]); setTagInput('') }
  }
  function changeTagInput(value: string) {
    setTagsLoading(true); setTagSuggestions([]); setTagError(''); setTagInput(value)
  }
  function applyTag() {
    const next = tagInput.trim()
    if (next !== tag) { setLoading(true); setTag(next) }
  }
  function showDismissedTab(value: boolean) {
    if (value === showDismissed) return
    setLoading(true); setJobs({ items: [], hasMore: false }); setShowDismissed(value)
    if (value) { setRetryId(null); setConsent(false); setAudioConsent(false) }
  }
  function startRetry(id: string) {
    setConsent(false); setAudioConsent(false); setRetryId(id)
  }
  function cancelRetry() {
    setRetryId(null); setConsent(false); setAudioConsent(false)
  }
  function showOriginal() {
    setShowRussian(false); setConfirmTranslation(false); showWhenReady.current = false
  }
  function selectRussian() {
    if (translation?.status === 'complete' && translation.document) { setShowRussian(true); setConfirmTranslation(false) } else setConfirmTranslation(true)
  }
  function checkTranslationAgain() {
    setTranslationLoading(true); setTranslationRefresh(value => value + 1)
  }
  function cancelTranslationConfirm() {
    setConfirmTranslation(false); setTranslationError(''); russianLanguageButton.current?.focus()
  }
  function editRecipe() {
    setShowRussian(false); setConfirmTranslation(false); showWhenReady.current = false
    if (original) startEdit(original)
  }

  return <div className="shell">
    <a className="skip-link" href="#main-content" onClick={e => { e.preventDefault(); document.getElementById('main-content')?.focus() }}>Skip to content</a>
    <header className="top"><a href="#/" className="brand"><span className="brand-mark">e</span> ezcooking</a>
      <nav aria-label="Main navigation"><a href="#/" aria-current={route.page === 'collection' ? 'page' : undefined}>Collection</a><a href="#/processing" aria-current={route.page === 'processing' ? 'page' : undefined}>Processing</a><a className="nav-add" href="#/add" aria-current={route.page === 'add' ? 'page' : undefined}>Add video</a></nav></header>
    <main id="main-content" tabIndex={-1}>
      {error && !editing && <div className="error" role="alert">{error}<button type="button" className="dismiss" onClick={() => setError('')} aria-label="Dismiss error">×</button></div>}
      {route.page === 'collection' && <CollectionPage
        search={search} tag={tag} tagInput={tagInput} tagSuggestions={tagSuggestions} tagsLoading={tagsLoading}
        tagError={tagError} loading={loading} failed={collectionFailed} collection={collection} busy={busy}
        onSearch={changeSearch} onSelectTag={selectTag} onClearTag={clearTag} onTagInput={changeTagInput}
        onApplyTag={applyTag} onMore={moreCollection} />}
      {route.page === 'add' && <AddPage
        url={url} hasFile={file !== null} fileInput={fileInput} consent={consent} audioConsent={audioConsent} busy={busy}
        onUrl={setUrl} onFile={setFile} onConsent={setConsent} onAudioConsent={setAudioConsent} onCreate={create} />}
      {route.page === 'processing' && <ProcessingPage
        jobs={jobs} pollStates={pollStates} showDismissed={showDismissed} retryId={retryId} consent={consent}
        audioConsent={audioConsent} busy={busy} loading={loading} onRefresh={refreshJobs} onShowDismissed={showDismissedTab}
        onMore={moreJobs} onStartRetry={startRetry} onCancelRetry={cancelRetry} onRetry={retry}
        onDismiss={id => dismiss(id, !showDismissed)} onDelete={id => remove(id, false)}
        onConsent={setConsent} onAudioConsent={setAudioConsent} />}
      {route.page === 'detail' && <section className="detail">
        <a href="#/">← Back to collection</a>
        {loading ? <p role="status">Loading recipe…</p> : !data || !selected ? <p>Recipe unavailable.</p> :
          editing ? <RecipeEditor
            title={title} setTitle={setTitle} ingredients={ingredients} setIngredients={setIngredients}
            steps={steps} setSteps={setSteps} tags={tags} setTags={setTags} busy={busy}
            changes={editorChanges} original={original} warnings={selected.warnings}
            error={error} notice={editorNotice} setNotice={setEditorNotice} newRowKey={newRowKey}
            focusRow={focusRow} onSubmit={save} onDiscard={discardEdit} /> :
          <RecipeDetailPage
            recipe={selected} data={data} largeScreen={largeScreen} videoError={videoError} busy={busy}
            translation={translation} translationLoading={translationLoading} translationError={translationError}
            translationBusy={translationBusy} confirmTranslation={confirmTranslation} showRussian={showRussian}
            originalButtonRef={originalLanguageButton} russianButtonRef={russianLanguageButton}
            onVideoError={() => setVideoError(true)} onShowOriginal={showOriginal} onSelectRussian={selectRussian}
            onCheckAgain={checkTranslationAgain} onRetryTranslation={() => setConfirmTranslation(true)}
            onCancelConfirm={cancelTranslationConfirm} onRequestTranslation={requestTranslation}
            onEdit={editRecipe} onDelete={() => remove(selected.id, true)} />}
      </section>}
    </main>
    <footer>Review recipes and food safety guidance before cooking.</footer>
  </div>
}
export default App
