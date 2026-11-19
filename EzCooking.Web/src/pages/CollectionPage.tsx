import type { Page, Summary } from '../types'
import { Button } from '../components/Button'
import { Pill } from '../components/Pill'

type CollectionPageProps = {
  search: string
  tag: string
  tagInput: string
  tagSuggestions: string[]
  tagsLoading: boolean
  tagError: string
  loading: boolean
  failed: boolean
  collection: Page<Summary>
  busy: boolean
  onSearch: (value: string) => void
  onSelectTag: (value: string) => void
  onClearTag: () => void
  onTagInput: (value: string) => void
  onApplyTag: () => void
  onMore: () => void
}

export function CollectionPage({ search, tag, tagInput, tagSuggestions, tagsLoading, tagError, loading, failed,
  collection, busy, onSearch, onSelectTag, onClearTag, onTagInput, onApplyTag, onMore }: CollectionPageProps) {
  return <section className="collection">
    <div className="collection-heading"><div><h1>Your recipes</h1><p>A little inspiration for your next meal.</p></div></div>
    <div className="collection-controls">
      <label className="search"><span>Search recipes</span><span className="search-field"><svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8"><circle cx="10.8" cy="10.8" r="6.8"/><path d="m16 16 4 4"/></svg><input type="search" value={search} disabled={busy} onChange={e => onSearch(e.target.value)} placeholder="Search by dish, ingredient or tag" maxLength={200} /></span></label>
      <div className="tag-browse" aria-label="Tag suggestions">
        <div className="tag-heading"><p>{tagInput.trim() ? 'Matching tags' : 'Explore by tag'}</p>{tag && <button type="button" className="clear-tag" disabled={busy} onClick={onClearTag}>Clear filter</button>}</div>
        {tagsLoading ? <span role="status">Finding tags…</span> : !tagError && tagSuggestions.length === 0 ? <span>{tagInput.trim() ? 'No matching saved tags.' : 'No saved tags yet.'}</span> :
          <div className="tag-options">{tagSuggestions.slice(0, 8).map(item => <Pill key={item} onClick={() => onSelectTag(item)} pressed={tag.toLocaleLowerCase() === item.toLocaleLowerCase()} disabled={busy}>{item}</Pill>)}</div>}
        <details className="more-tags"><summary>{tagInput.trim() ? 'Edit tag search' : 'Find another tag'}</summary><form className="tag-filter" onSubmit={e => { e.preventDefault(); onApplyTag() }}>
          <label htmlFor="collection-tag">Search tags<input id="collection-tag" type="search" value={tagInput} disabled={busy} onChange={e => onTagInput(e.target.value)} placeholder="Type a tag" maxLength={200} /></label>
          <Button type="submit" variant="secondary" disabled={busy || !tagInput.trim()}>Apply tag</Button>
        </form>{tagSuggestions.length > 8 && !tagsLoading && <p className="fine">Showing 8 tags. Type more to narrow the list.</p>}</details>
      </div>
      {tagError && <p className="job-error" role="alert">{tagError}</p>}
    </div>
    <div className="collection-results"><h2>{tag ? <>Recipes tagged “{tag}”</> : 'Your collection'}</h2><p>Newest first</p></div>
    {loading ? <p className="empty" role="status">Loading recipes…</p> : failed ? <p className="empty">Recipes could not be loaded. Change the filters or reload the page to try again.</p> : collection.items.length === 0 ? <p className="empty">{search || tag ? 'No recipes match these filters. Try another search or clear the tag.' : <>No saved recipes yet. <a href="#/add">Add your first video</a>.</>}</p> :
      <div className="recipe-grid">{collection.items.map(r => <a key={r.id} className="tile" href={`#/recipes/${r.id}`}>
        {r.cover ? <img src={r.cover} alt="" loading="lazy" /> : <div className="placeholder">…</div>}
        <span className="tile-copy"><strong>{r.title}</strong><small>{r.needsReview ? 'Needs review' : r.tags.slice(0, 2).join('  /  ') || 'Saved recipe'}</small></span></a>)}</div>}
    {!loading && !failed && collection.hasMore && <Button variant="secondary" className="load-more" disabled={busy} onClick={onMore}>Load more recipes</Button>}
  </section>
}
