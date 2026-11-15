import { useEffect, useRef, useState } from 'react'
import './App.css'

type Ingredient = { name: string; estimatedAmount: string | null }
type Step = { text: string; timestampSeconds: number | null }
type EditableIngredient = { key: number; name: string; amount: string }
type EditableStep = { key: number; text: string; originalText: string; timestampSeconds: number | null }
type Document = { title: string; ingredients: Ingredient[]; steps: Step[]; uncertainties: string[]; tags: string[]; cover: string; frames: string[]; video: string; attribution: string | null; model: string; cost: number | null; sourceCaption?: string | null; audioModel?: string | null; audioCost?: number | null; recipeSource?: 'caption' | 'video' | 'edited' | null }
type Activity = { at: string; message: string }
type Recipe = { id: string; sourceUrl: string | null; status: string; error: string | null; document: string | null; version: number; createdAt: string; warnings: string[]; activity?: Activity[] | null }
type Translation = { version: number; status: 'queued' | 'processing' | 'complete' | 'failed'; document: Document | null; error: string | null }
type PollState = { checkedAt: number | null; unavailable: boolean }
type Summary = { id: string; title: string; cover: string; tags: string[]; needsReview: boolean }
type Page<T> = { items: T[]; hasMore: boolean }
type Route = { page: 'collection' | 'add' | 'processing' | 'detail'; id?: string }

function routeFromHash(): Route {
  const hash = window.location.hash
  if (hash === '#/add') return { page: 'add' }
  if (hash === '#/processing') return { page: 'processing' }
  const match = hash.match(/^#\/recipes\/([a-f0-9-]+)$/i)
  return match ? { page: 'detail', id: match[1] } : { page: 'collection' }
}
async function api<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new Error(body?.detail || `Request failed (${response.status}).`)
  }
  return response.status === 204 ? undefined as T : response.json()
}
const json = (body: unknown): RequestInit => ({ method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
const message = (error: unknown) => error instanceof Error ? error.message : 'Something went wrong.'
const documentOf = (recipe: Recipe): Document | null => recipe.document ? JSON.parse(recipe.document) as Document : null
function App() {
  const [route, setRoute] = useState<Route>(routeFromHash)
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
  const originalLanguageButton = useRef<HTMLButtonElement>(null)
  const russianLanguageButton = useRef<HTMLButtonElement>(null)
  const [editVersion, setEditVersion] = useState(0)
  const [title, setTitle] = useState('')
  const [ingredients, setIngredients] = useState<EditableIngredient[]>([])
  const [steps, setSteps] = useState<EditableStep[]>([])
  const nextRowKey = useRef(0)
  const editorFocus = useRef<string | null>(null)
  const [editorNotice, setEditorNotice] = useState('')
  const [tags, setTags] = useState('')
  const original = selected ? documentOf(selected) : null
  const data = showRussian && translation && translation.version === selected?.version && translation.document ? translation.document : original
  const translationRecipeId = selected?.id
  const translationRecipeVersion = selected?.version
  const translationRecipeStatus = selected?.status
  const activeJobIds = jobs.items.filter(j => ['staging', 'queued', 'processing'].includes(j.status)).map(j => j.id).join(',')

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
    const onHash = () => { setRoute(routeFromHash()); setError(''); setLoading(true); setSelected(null); setVideoError(false); setEditing(false); setRetryId(null); setConsent(false); setAudioConsent(false); setTranslation(null); setTranslationLoading(true); setTranslationError(''); setConfirmTranslation(false); setShowRussian(false); showWhenReady.current = false }
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])
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
  async function create(e: React.FormEvent) {
    e.preventDefault(); setBusy(true); setError('')
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
  function startEdit(d: Document) {
    setEditVersion(selected?.version ?? 0)
    setTitle(d.title)
    setIngredients(d.ingredients.map(i => ({ key: nextRowKey.current++, name: i.name, amount: i.estimatedAmount ?? '' })))
    setSteps(d.steps.map(s => ({ key: nextRowKey.current++, text: s.text, originalText: s.text, timestampSeconds: s.timestampSeconds })))
    setTags(d.tags.join(', ')); setEditorNotice(''); setError(''); setEditing(true)
  }
  function moveRow<T>(items: T[], index: number, direction: -1 | 1): T[] {
    const target = index + direction
    if (target < 0 || target >= items.length) return items
    const result = [...items]
    ;[result[index], result[target]] = [result[target], result[index]]
    return result
  }
  async function save(e: React.FormEvent) {
    e.preventDefault()
    if (!selected) return
    setBusy(true); setError('')
    try {
      const body = { version: editVersion, title, ingredients: ingredients.map(i => ({
        name: i.name.trim(), amount: i.amount.trim() || null, evidence: 'edited'
      })), steps: steps.map(s => ({ text: s.text.trim(),
        timestampSeconds: s.text.trim() === s.originalText.trim() ? s.timestampSeconds : null, evidence: 'edited' })),
      tags: data && tags === data.tags.join(', ') ? data.tags : tags.split(',').map(t => t.trim()).filter(Boolean) }
      const updated = await api<Recipe>(`/api/recipes/${selected.id}`, { ...json(body), method: 'PUT' })
      setTranslation(null); setTranslationLoading(true); setConfirmTranslation(false); setShowRussian(false); showWhenReady.current = false; setSelected(updated)
      setEditing(false)
    } catch (e) { setError(message(e) === 'Request failed (409).' ? 'This recipe changed while you were editing. Reload before saving.' : message(e)) }
    finally { setBusy(false) }
  }
  const consentFields = <><p className="privacy">Frames and any public caption are sent to OpenRouter and its selected AI provider. Optional audio is sent to an audio-capable provider. Only share content you have permission to use.</p>
    <label className="check"><input type="checkbox" checked={consent} onChange={e => setConsent(e.target.checked)} /> I consent to sending this video’s frames and public caption for analysis.</label>
    <label className="check"><input type="checkbox" checked={audioConsent} onChange={e => setAudioConsent(e.target.checked)} /> I also consent to sending this video’s audio.</label></>
  const videoPlayer = data?.video ? <div className="recipe-player">
    <video controls preload="metadata" poster={data.cover || undefined} src={data.video} aria-label={`Original video for ${data.title}`} onError={() => setVideoError(true)}>Your browser cannot play this video.</video>
    {videoError && <p className="job-error" role="alert">The saved video could not be played. {selected?.sourceUrl ? 'Try the original Instagram reel.' : 'The local video may be unavailable.'}</p>}
  </div> : <p className="fine">The saved video is unavailable.</p>

  return <div className="shell">
    <a className="skip-link" href="#main-content" onClick={e => { e.preventDefault(); document.getElementById('main-content')?.focus() }}>Skip to content</a>
    <header className="top"><a href="#/" className="brand"><span className="brand-mark">e</span> ezcooking</a>
      <nav aria-label="Main navigation"><a href="#/" aria-current={route.page === 'collection' ? 'page' : undefined}>Collection</a><a href="#/processing" aria-current={route.page === 'processing' ? 'page' : undefined}>Processing</a><a className="nav-add" href="#/add" aria-current={route.page === 'add' ? 'page' : undefined}>Add video</a></nav></header>
    <main id="main-content" tabIndex={-1}>
      {error && <div className="error" role="alert">{error}<button type="button" className="dismiss" onClick={() => setError('')} aria-label="Dismiss error">×</button></div>}
      {route.page === 'collection' && <section className="collection">
        <div className="collection-heading"><div><h1>Your recipes</h1><p>A little inspiration for your next meal.</p></div><a className="collection-add" href="#/add"><span aria-hidden="true">+</span> Add a video</a></div>
        <div className="collection-controls">
          <label className="search"><span>Search recipes</span><span className="search-field"><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><circle cx="10.8" cy="10.8" r="6.8"/><path d="m16 16 4 4"/></svg><input type="search" value={search} disabled={busy} onChange={e => { setLoading(true); setSearch(e.target.value) }} placeholder="Search by dish, ingredient or tag" maxLength={200} /></span></label>
          <div className="tag-browse" aria-label="Tag suggestions">
            <div className="tag-heading"><p>{tagInput.trim() ? 'Matching tags' : 'Explore by tag'}</p>{tag && <button type="button" className="clear-tag" disabled={busy} onClick={() => { setLoading(true); setTag(''); if (tagInput) { setTagsLoading(true); setTagSuggestions([]); setTagInput('') } }}>Clear filter</button>}</div>
            {tagsLoading ? <span role="status">Finding tags…</span> : !tagError && tagSuggestions.length === 0 ? <span>{tagInput.trim() ? 'No matching saved tags.' : 'No saved tags yet.'}</span> :
              <div className="tag-options">{tagSuggestions.slice(0, 8).map(item => <button type="button" className="secondary tag-option" key={item} disabled={busy} aria-pressed={tag.toLocaleLowerCase() === item.toLocaleLowerCase()} onClick={() => { if (item !== tag) { setLoading(true); setTag(item) } }}>{item}</button>)}</div>}
            <details className="more-tags"><summary>{tagInput.trim() ? 'Edit tag search' : 'Find another tag'}</summary><form className="tag-filter" onSubmit={e => { e.preventDefault(); const next = tagInput.trim(); if (next !== tag) { setLoading(true); setTag(next) } }}>
              <label htmlFor="collection-tag">Search tags<input id="collection-tag" type="search" value={tagInput} disabled={busy} onChange={e => { setTagsLoading(true); setTagSuggestions([]); setTagError(''); setTagInput(e.target.value) }} placeholder="Type a tag" maxLength={200} /></label>
              <button type="submit" className="secondary" disabled={busy || !tagInput.trim()}>Apply tag</button>
            </form>{tagSuggestions.length > 8 && !tagsLoading && <p className="fine">Showing 8 tags. Type more to narrow the list.</p>}</details>
          </div>
          {tagError && <p className="job-error" role="alert">{tagError}</p>}
        </div>
        <div className="collection-results"><h2>{tag ? <>Recipes tagged “{tag}”</> : 'Your collection'}</h2><p>Newest first</p></div>
        {loading ? <p className="empty" role="status">Loading recipes…</p> : collectionFailed ? <p className="empty">Recipes could not be loaded. Change the filters or reload the page to try again.</p> : collection.items.length === 0 ? <p className="empty">{search || tag ? 'No recipes match these filters. Try another search or clear the tag.' : <>No saved recipes yet. <a href="#/add">Add your first video</a>.</>}</p> :
          <div className="recipe-grid">{collection.items.map(r => <a key={r.id} className="tile" href={`#/recipes/${r.id}`}>
            {r.cover ? <img src={r.cover} alt="" loading="lazy" /> : <div className="placeholder">…</div>}
            <span className="tile-copy"><strong>{r.title}</strong><small>{r.needsReview ? 'Needs review' : r.tags.slice(0, 2).join('  /  ') || 'Saved recipe'}</small></span></a>)}</div>}
        {!loading && !collectionFailed && collection.hasMore && <button className="secondary load-more" disabled={busy} onClick={moreCollection}>Load more recipes</button>}
      </section>}
      {route.page === 'add' && <section className="narrow page">
        <p className="intro-note">From video to cookbook</p><h1>Add a cooking video</h1><p>Paste a public reel link or upload a video you can use.</p>
        <form className="capture" onSubmit={create}>
          <label htmlFor="url">Instagram reel link <small>(optional with upload)</small></label>
          <input id="url" type="url" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://www.instagram.com/reel/…" />
          <div className="or">or upload a video</div><input ref={fileInput} aria-label="Upload a video" type="file" accept="video/*" onChange={e => setFile(e.target.files?.[0] ?? null)} />
          {consentFields}<button type="submit" disabled={busy || !consent || (!url && !file)}>{busy ? 'Adding…' : 'Create recipe'}</button>
        </form></section>}
      {route.page === 'processing' && <section className="narrow page">
        <p className="intro-note">Your videos</p><h1>Processing</h1><p>Track videos being prepared and resolve any that need attention. Refresh to see new videos sent from Telegram.</p>
        <div className="processing-toolbar"><button className="secondary" disabled={busy || loading} onClick={refreshJobs}>{busy ? 'Please wait…' : 'Refresh videos'}</button><span>Refresh shows the first page.</span></div>
        <div className="tabs"><button disabled={busy} className={!showDismissed ? '' : 'secondary'} onClick={() => { if (showDismissed) { setLoading(true); setJobs({ items: [], hasMore: false }); setShowDismissed(false) } }}>Current</button><button disabled={busy} className={showDismissed ? '' : 'secondary'} onClick={() => { if (!showDismissed) { setLoading(true); setJobs({ items: [], hasMore: false }); setShowDismissed(true); setRetryId(null); setConsent(false); setAudioConsent(false) } }}>Dismissed</button></div>
        {loading ? <p role="status">Loading videos…</p> : jobs.items.length === 0 ? <p className="empty">{showDismissed ? 'No dismissed videos.' : <>No videos need attention. <a href="#/add">Add a video</a> or share a reel with your paired Telegram bot.</>}</p> :
          <div className="job-list">{jobs.items.map(job => <article className={`job${job.status === 'processing' ? ' job-processing' : ''}${job.status === 'processing' && !pollStates[job.id]?.unavailable ? ' job-live' : ''}`} key={job.id}>
            <div className="job-head"><strong aria-live="polite">{job.status === 'complete' ? 'Recipe ready' : job.status === 'delete_failed' ? 'Deletion needs attention' : job.status === 'deleting' ? 'Deleting' : job.status === 'failed' ? 'Needs attention' : job.status === 'processing' ? 'Preparing recipe' : 'Waiting to process'}</strong><span>{new Date(job.createdAt).toLocaleString()}</span></div>
            {job.sourceUrl ? <a className="job-source" href={job.sourceUrl} target="_blank" rel="noreferrer">{job.sourceUrl}</a> : <p>Uploaded video</p>}
            {['staging', 'queued', 'processing'].includes(job.status) && <div className="job-activity">
              <p className="activity-current" role="status">{job.status === 'staging' ? 'Preparing the upload.' : job.status === 'queued' ? 'In the queue. Videos are processed one at a time.' : job.activity?.at(-1)?.message ?? 'Waiting for the first activity update.'}</p>
              {job.status === 'processing' && job.activity && job.activity.length > 0 && <p className="activity-time">Last activity: {new Date(job.activity.at(-1)!.at).toLocaleTimeString()}</p>}
              <p role={pollStates[job.id]?.unavailable ? 'status' : undefined} className={pollStates[job.id]?.unavailable ? 'activity-disconnected' : 'activity-time'}>{pollStates[job.id]?.unavailable
                ? 'Live updates unavailable. The app will keep trying.'
                : pollStates[job.id]?.checkedAt ? `Status checked at ${new Date(pollStates[job.id].checkedAt!).toLocaleTimeString()}. Activity changes at processing milestones.` : 'Checking for updates…'}</p>
              {job.status === 'processing' && job.activity && job.activity.length > 1 && <details className="activity-history"><summary>Recent activity</summary><ol>{job.activity.map((event, index) => <li key={`${event.at}-${index}`}><time dateTime={event.at}>{new Date(event.at).toLocaleTimeString()}</time><span>{event.message}</span></li>)}</ol></details>}
            </div>}
            {job.status === 'complete' && <p><a href={`#/recipes/${job.id}`}>Open saved recipe</a></p>}
            {job.error && <div className="job-error"><strong>What happened</strong><p>{job.error}</p></div>}
            {job.status === 'failed' && <>{!showDismissed && <p className="fine">Retry after resolving the issue. To upload a replacement with the same link, permanently delete this failed video first, then <a href="#/add">add the replacement</a>.</p>}
              {!showDismissed && (retryId === job.id ? <div className="retry">{consentFields}<div className="actions"><button disabled={busy || !consent} onClick={() => retry(job.id)}>Retry with consent</button><button className="secondary" disabled={busy} onClick={() => { setRetryId(null); setConsent(false); setAudioConsent(false) }}>Cancel</button></div></div> : <button className="retry" disabled={busy} onClick={() => { setConsent(false); setAudioConsent(false); setRetryId(job.id) }}>Retry this video</button>)}
              <div className="actions job-secondary-actions">
              <button className="secondary" disabled={busy} onClick={() => dismiss(job.id, !showDismissed)}>{showDismissed ? 'Restore' : 'Dismiss'}</button>
              <button className="secondary" disabled={busy} onClick={() => remove(job.id, false)}>Delete video</button>
              </div><p className="fine job-retention">{showDismissed ? 'Restoring returns this video to Current.' : 'Dismiss hides this video without deleting it.'} Delete permanently removes the video and its local photos.</p></>}
            {job.status === 'delete_failed' && <button className="secondary" disabled={busy} onClick={() => remove(job.id, false)}>Retry deletion</button>}
          </article>)}</div>}
        {jobs.hasMore && <button className="secondary load-more" disabled={busy} onClick={moreJobs}>Load more videos</button>}
      </section>}
      {route.page === 'detail' && <section className="detail">
        <a href="#/">← Back to collection</a>
        {loading ? <p role="status">Loading recipe…</p> : !data || !selected ? <p>Recipe unavailable.</p> :
          editing ? <form onSubmit={save} className="editor"><h1>Correct recipe</h1>
            <p className="fine">Change only what needs correcting. Source information and uncertainties are kept.</p>
            <label>Title<input value={title} onChange={e => setTitle(e.target.value)} maxLength={200} disabled={busy} required /></label>
            <fieldset className="edit-group"><legend>Ingredients</legend><p className="fine">Leave an amount blank if it is unknown.</p>
              {ingredients.map((item, index) => <div className="edit-row" key={item.key}>
                <div className="edit-row-heading"><strong>Ingredient {index + 1}</strong></div>
                <div className="ingredient-fields"><label>Name<input id={`ingredient-${item.key}`} aria-label={`Ingredient ${index + 1} name`} value={item.name} maxLength={150} disabled={busy} required onChange={e => setIngredients(old => old.map(row => row.key === item.key ? { ...row, name: e.target.value } : row))} /></label>
                  <label>Amount <small>(optional)</small><input aria-label={`Ingredient ${index + 1} amount`} value={item.amount} maxLength={100} disabled={busy} onChange={e => setIngredients(old => old.map(row => row.key === item.key ? { ...row, amount: e.target.value } : row))} /></label></div>
                <div className="actions row-actions">
                  <button type="button" className="secondary" disabled={busy || index === 0} aria-label={`Move ingredient ${index + 1} up`} onClick={() => { editorFocus.current = `ingredient-${item.key}`; setIngredients(old => moveRow(old, index, -1)); setEditorNotice(`Ingredient moved to position ${index}.`) }}>Move up</button>
                  <button type="button" className="secondary" disabled={busy || index === ingredients.length - 1} aria-label={`Move ingredient ${index + 1} down`} onClick={() => { editorFocus.current = `ingredient-${item.key}`; setIngredients(old => moveRow(old, index, 1)); setEditorNotice(`Ingredient moved to position ${index + 2}.`) }}>Move down</button>
                  <button type="button" className="secondary" disabled={busy || ingredients.length === 1} aria-label={`Remove ingredient ${index + 1}`} onClick={() => { editorFocus.current = `ingredient-${ingredients[index + 1]?.key ?? ingredients[index - 1].key}`; setIngredients(old => old.filter(row => row.key !== item.key)); setEditorNotice(`Ingredient ${index + 1} removed.`) }}>Remove</button>
                </div>
              </div>)}
              <button type="button" className="secondary" disabled={busy || ingredients.length >= 100} onClick={() => { const key = nextRowKey.current++; editorFocus.current = `ingredient-${key}`; setIngredients(old => [...old, { key, name: '', amount: '' }]); setEditorNotice('Ingredient added.') }}>Add ingredient</button>
            </fieldset>
            <fieldset className="edit-group"><legend>Instructions</legend><p className="fine">Describe one cooking action per step. Editing a step clears its video timestamp.</p>
              {steps.map((item, index) => <div className="edit-row" key={item.key}>
                <label>Step {index + 1}<textarea id={`step-${item.key}`} aria-label={`Step ${index + 1}`} value={item.text} maxLength={2000} rows={3} disabled={busy} required onChange={e => setSteps(old => old.map(row => row.key === item.key ? { ...row, text: e.target.value } : row))} /></label>
                <div className="actions row-actions">
                  <button type="button" className="secondary" disabled={busy || index === 0} aria-label={`Move step ${index + 1} up`} onClick={() => { editorFocus.current = `step-${item.key}`; setSteps(old => moveRow(old, index, -1)); setEditorNotice(`Step moved to position ${index}.`) }}>Move up</button>
                  <button type="button" className="secondary" disabled={busy || index === steps.length - 1} aria-label={`Move step ${index + 1} down`} onClick={() => { editorFocus.current = `step-${item.key}`; setSteps(old => moveRow(old, index, 1)); setEditorNotice(`Step moved to position ${index + 2}.`) }}>Move down</button>
                  <button type="button" className="secondary" disabled={busy || steps.length === 1} aria-label={`Remove step ${index + 1}`} onClick={() => { editorFocus.current = `step-${steps[index + 1]?.key ?? steps[index - 1].key}`; setSteps(old => old.filter(row => row.key !== item.key)); setEditorNotice(`Step ${index + 1} removed.`) }}>Remove</button>
                </div>
              </div>)}
              <button type="button" className="secondary" disabled={busy || steps.length >= 100} onClick={() => { const key = nextRowKey.current++; editorFocus.current = `step-${key}`; setSteps(old => [...old, { key, text: '', originalText: '', timestampSeconds: null }]); setEditorNotice('Step added.') }}>Add step</button>
            </fieldset>
            <p className="sr-only" role="status">{editorNotice}</p>
            <label>Tags, separated by commas<input value={tags} onChange={e => setTags(e.target.value)} disabled={busy} /></label>
            <div className="actions"><button disabled={busy}>Save changes</button><button type="button" className="secondary" disabled={busy} onClick={() => { setError(''); setEditing(false) }}>Cancel</button></div></form> :
          <><div className="detail-head"><div><h1>{data.title}</h1>{data.tags.length > 0 && <p className="detail-tags">{data.tags.join(' · ')}</p>}
              <div className="translation">
                <div className="language-control" role="group" aria-label="Recipe language">
                  <button ref={originalLanguageButton} type="button" aria-pressed={!showRussian} onClick={() => { setShowRussian(false); setConfirmTranslation(false); showWhenReady.current = false }}>Original</button>
                  <button ref={russianLanguageButton} type="button" lang="ru" aria-pressed={showRussian} aria-expanded={translation?.status === 'complete' ? undefined : confirmTranslation} aria-controls={confirmTranslation ? 'translation-confirm' : undefined} disabled={translationLoading || translationBusy || translation?.status === 'queued' || translation?.status === 'processing' || (!!translationError && !confirmTranslation)}
                    onClick={() => { if (translation?.status === 'complete' && translation.document) { setShowRussian(true); setConfirmTranslation(false) } else setConfirmTranslation(true) }}>Русский</button>
                </div>
                {translationLoading ? <p className="translation-status" role="status">Checking Russian translation…</p> :
                  translation?.status === 'queued' || translation?.status === 'processing' ? <p className="translation-status" role="status">Russian translation {translation.status === 'queued' ? 'queued' : 'in progress'}. You can leave this page.</p> :
                  showRussian ? <p className="translation-status" role="status">AI translation. Check the original before cooking.</p> :
                  translation?.status === 'complete' && translation.document ? <p className="translation-status" role="status">Russian translation available.</p> :
                  !translation && !translationError && !confirmTranslation ? <p className="translation-status">Russian translation not yet available.</p> : null}
                {translationError && <div className="translation-message"><p role="alert">{translationError}</p>
                  {!confirmTranslation && <button type="button" className="secondary" onClick={() => { setTranslationLoading(true); setTranslationRefresh(value => value + 1) }}>Check again</button>}</div>}
                {translation?.status === 'failed' && !confirmTranslation && !translationError && <div className="translation-message"><p role="alert">Russian translation unavailable: {translation.error ?? 'Translation failed.'}</p><button type="button" className="secondary" onClick={() => setConfirmTranslation(true)}>Retry translation</button></div>}
                {confirmTranslation && <div className="translation-confirm" id="translation-confirm">
                  <p><strong>Russian translation is not available yet.</strong></p>
                  <p>Send this recipe’s text to OpenRouter and its selected provider? This uses a separate free-model request.</p>
                  <div className="actions"><button type="button" disabled={translationBusy} onClick={requestTranslation}>{translationBusy ? 'Sending…' : translationError ? 'Retry sending' : 'Translate and send'}</button><button type="button" className="secondary" disabled={translationBusy} onClick={() => { setConfirmTranslation(false); setTranslationError(''); russianLanguageButton.current?.focus() }}>Cancel</button></div>
                </div>}
              </div>
            </div><div className="actions"><button className="secondary" disabled={busy} onClick={() => { setShowRussian(false); setConfirmTranslation(false); showWhenReady.current = false; if (original) startEdit(original) }}>Correct recipe</button><button className="secondary" disabled={busy} onClick={() => remove(selected.id, true)}>Delete recipe</button></div></div>
            <div className="recipe-intro">{largeScreen && data.video ? videoPlayer : <img className="cover" src={data.cover} alt={`Frame from the video for ${data.title}`} />}<div className="recipe-context"><h2>About this recipe</h2>
              <p className="evidence">{data.recipeSource === 'caption' ? 'The method comes from the public caption. Ingredients and amounts were organized by AI and may be incomplete or misinterpreted. Check the original before cooking.' : data.recipeSource === 'edited' ? 'This recipe was edited. Check it against the original before cooking.' : 'The photos are video frames. Ingredients and actions are AI inferences, not independently confirmed source details. Check the original before cooking.'}</p>
              {data.attribution && <p className="source-attribution">Source: {data.attribution}</p>}
              {selected.sourceUrl && <a href={selected.sourceUrl} target="_blank" rel="noreferrer">Open the original Instagram reel</a>}
            </div></div>
            {selected.warnings.length > 0 && <div className="error recipe-warning"><div><strong>This draft needs review.</strong><ul>{selected.warnings.map((w, n) => <li key={n}>{w}</li>)}</ul><p>Check the original or correct the recipe.</p></div></div>}
            <div className="recipe-body"><section className="ingredients-section"><h2>Ingredients</h2><p className="fine ingredient-note">{data.recipeSource === 'caption' ? 'Amounts were extracted from the caption; verify each ingredient and amount against the original.' : data.recipeSource === 'edited' ? 'Amounts may include user edits and unverified AI suggestions.' : 'Amounts marked ~ are estimates, not the creator’s exact quantities.'}</p><ul className="ingredient-list">{data.ingredients.map((i, n) => <li key={n}><span>{i.name}</span><span>{i.estimatedAmount ? data.recipeSource === 'caption' || data.recipeSource === 'edited' ? i.estimatedAmount : `~ ${i.estimatedAmount}` : 'Amount unknown'}</span></li>)}</ul></section>
            <section className="method-section"><h2>Method</h2><ol className="method">{data.steps.map((s, n) => <li key={n}>{s.text}</li>)}</ol><div className="uncertainty"><h2>Before you cook</h2>{data.uncertainties.length > 0 && <ul>{data.uncertainties.map((u, n) => <li key={n}>{u}</li>)}</ul>}<p>Check cooking temperatures and food handling against a trusted source.</p></div></section></div>
            <section className="recipe-source"><h2>{largeScreen && data.video ? 'Original source' : 'Original video'}</h2><div className="source-layout">{(!largeScreen || !data.video) && videoPlayer}<div><p>Review the video alongside the recipe.</p>{selected.sourceUrl && <p><a href={selected.sourceUrl} target="_blank" rel="noreferrer">Open the original Instagram reel</a></p>}{data.attribution && <p className="fine">Source: {data.attribution}</p>}</div></div>
              {data.sourceCaption && <p className="fine caption"><strong>Public caption:</strong> {data.sourceCaption}</p>}
              <details className="analysis-details"><summary>More frames and analysis details</summary><div className="frames">{data.frames.map(f => <img key={f} src={f} alt="Frame from the source video" loading="lazy" />)}</div><p className="fine">Vision model: {data.model}. {data.audioModel && `Audio model: ${data.audioModel}. Audio reported cost: ${data.audioCost == null ? 'not provided' : `$${data.audioCost}`}. `}Total reported cost: {data.cost == null ? 'not provided' : `$${data.cost}`}.</p></details>
            </section>
          </>}
      </section>}
    </main>
    <footer>Review recipes and food safety guidance before cooking.</footer>
  </div>
}
export default App
