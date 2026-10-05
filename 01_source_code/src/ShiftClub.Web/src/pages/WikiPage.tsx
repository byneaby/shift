import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiFetch, apiUpload } from '../api/client'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import { isOwner } from '../permissions'

type WikiListItem = {
  id: string
  slug: string
  title: string
  sortOrder: number
  updatedAt?: string
}

type WikiPageDto = {
  id: string
  slug: string
  title: string
  bodyMarkdown: string
  sortOrder: number
  createdAt: string
  updatedAt?: string
}

function escHtml(s: string) {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/** Лёгкий Markdown → HTML для инструкций (заголовки, списки, картинки, ссылки, цитаты, таблицы). */
export function renderWikiMarkdown(md: string): string {
  const lines = (md || '').replace(/\r\n/g, '\n').split('\n')
  const out: string[] = []
  let i = 0
  let inUl = false
  let inOl = false
  let inTable = false

  const closeLists = () => {
    if (inUl) {
      out.push('</ul>')
      inUl = false
    }
    if (inOl) {
      out.push('</ol>')
      inOl = false
    }
  }
  const closeTable = () => {
    if (inTable) {
      out.push('</tbody></table>')
      inTable = false
    }
  }

  const inline = (text: string) => {
    let t = escHtml(text)
    t = t.replace(/!\[([^\]]*)\]\(([^)]+)\)/g, (_m, alt, src) => {
      const a = escHtml(alt)
      const s = String(src).replace(/&amp;/g, '&')
      return `<figure class="wiki-figure"><img src="${escHtml(s)}" alt="${a}" loading="lazy" /><figcaption>${a}</figcaption></figure>`
    })
    t = t.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" target="_blank" rel="noreferrer">$1</a>')
    t = t.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    t = t.replace(/`([^`]+)`/g, '<code>$1</code>')
    return t
  }

  while (i < lines.length) {
    const line = lines[i]
    const trimmed = line.trim()

    if (trimmed.startsWith('|') && trimmed.endsWith('|')) {
      closeLists()
      const cells = trimmed
        .slice(1, -1)
        .split('|')
        .map((c) => c.trim())
      const isSep = cells.every((c) => /^:?-+:?$/.test(c))
      if (isSep) {
        i++
        continue
      }
      if (!inTable) {
        out.push('<table class="wiki-table"><tbody>')
        inTable = true
        out.push(`<tr>${cells.map((c) => `<th>${inline(c)}</th>`).join('')}</tr>`)
      } else {
        out.push(`<tr>${cells.map((c) => `<td>${inline(c)}</td>`).join('')}</tr>`)
      }
      i++
      continue
    }
    closeTable()

    if (!trimmed) {
      closeLists()
      i++
      continue
    }

    if (trimmed.startsWith('```')) {
      closeLists()
      const buf: string[] = []
      i++
      while (i < lines.length && !lines[i].trim().startsWith('```')) {
        buf.push(escHtml(lines[i]))
        i++
      }
      i++
      out.push(`<pre class="wiki-pre"><code>${buf.join('\n')}</code></pre>`)
      continue
    }

    const h = /^(#{1,3})\s+(.+)$/.exec(trimmed)
    if (h) {
      closeLists()
      const level = h[1].length
      out.push(`<h${level}>${inline(h[2])}</h${level}>`)
      i++
      continue
    }

    if (trimmed.startsWith('> ')) {
      closeLists()
      out.push(`<blockquote>${inline(trimmed.slice(2))}</blockquote>`)
      i++
      continue
    }

    if (/^[-*]\s+/.test(trimmed)) {
      if (inOl) {
        out.push('</ol>')
        inOl = false
      }
      if (!inUl) {
        out.push('<ul>')
        inUl = true
      }
      out.push(`<li>${inline(trimmed.replace(/^[-*]\s+/, ''))}</li>`)
      i++
      continue
    }

    if (/^\d+\.\s+/.test(trimmed)) {
      if (inUl) {
        out.push('</ul>')
        inUl = false
      }
      if (!inOl) {
        out.push('<ol>')
        inOl = true
      }
      out.push(`<li>${inline(trimmed.replace(/^\d+\.\s+/, ''))}</li>`)
      i++
      continue
    }

    closeLists()
    // standalone image line already handled by inline figure
    if (/^!\[/.test(trimmed)) {
      out.push(inline(trimmed))
    } else {
      out.push(`<p>${inline(trimmed)}</p>`)
    }
    i++
  }
  closeLists()
  closeTable()
  return out.join('\n')
}

function WikiHtml({ markdown }: { markdown: string }) {
  const html = useMemo(() => renderWikiMarkdown(markdown), [markdown])
  return <div className="wiki-body" dangerouslySetInnerHTML={{ __html: html }} />
}

export function WikiPage() {
  const { slug: routeSlug } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const owner = isOwner()
  const fileRef = useRef<HTMLInputElement>(null)

  const [editOpen, setEditOpen] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)

  const [formSlug, setFormSlug] = useState('')
  const [formTitle, setFormTitle] = useState('')
  const [formBody, setFormBody] = useState('')
  const [formSort, setFormSort] = useState(100)

  const listQuery = useQuery({
    queryKey: ['staff-wiki'],
    queryFn: async () => (await apiFetch<WikiListItem[]>('/api/staff-wiki')).data ?? [],
  })

  const pages = listQuery.data ?? []
  const activeSlug = routeSlug || pages[0]?.slug || ''

  useEffect(() => {
    if (!routeSlug && pages[0]?.slug) {
      navigate(`/wiki/${pages[0].slug}`, { replace: true })
    }
  }, [routeSlug, pages, navigate])

  const pageQuery = useQuery({
    queryKey: ['staff-wiki', activeSlug],
    queryFn: async () => (await apiFetch<WikiPageDto>(`/api/staff-wiki/${encodeURIComponent(activeSlug)}`)).data!,
    enabled: !!activeSlug,
  })

  const page = pageQuery.data

  function openEdit() {
    if (!page) return
    setFormSlug(page.slug)
    setFormTitle(page.title)
    setFormBody(page.bodyMarkdown)
    setFormSort(page.sortOrder)
    setError(null)
    setEditOpen(true)
  }

  function openCreate() {
    setFormSlug('')
    setFormTitle('')
    setFormBody('# Новая статья\n\n')
    setFormSort((pages[pages.length - 1]?.sortOrder ?? 0) + 10)
    setError(null)
    setCreateOpen(true)
  }

  const saveMutation = useMutation({
    mutationFn: async (mode: 'create' | 'update') => {
      const body = {
        slug: formSlug,
        title: formTitle,
        bodyMarkdown: formBody,
        sortOrder: formSort,
      }
      if (mode === 'create') {
        return apiFetch<WikiPageDto>('/api/staff-wiki', { method: 'POST', body: JSON.stringify(body) })
      }
      return apiFetch<WikiPageDto>(`/api/staff-wiki/${page!.id}`, {
        method: 'PUT',
        body: JSON.stringify(body),
      })
    },
    onSuccess: async (res, mode) => {
      setFlash(mode === 'create' ? 'Статья создана' : 'Сохранено')
      setEditOpen(false)
      setCreateOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['staff-wiki'] })
      if (res.data?.slug) navigate(`/wiki/${res.data.slug}`)
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка сохранения'),
  })

  const deleteMutation = useMutation({
    mutationFn: async () => {
      if (!page) throw new Error('Нет статьи')
      return apiFetch(`/api/staff-wiki/${page.id}`, { method: 'DELETE' })
    },
    onSuccess: async () => {
      setDeleteOpen(false)
      setFlash('Статья удалена')
      await queryClient.invalidateQueries({ queryKey: ['staff-wiki'] })
      navigate('/wiki')
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Не удалось удалить'),
  })

  const uploadMutation = useMutation({
    mutationFn: async (file: File) => {
      const fd = new FormData()
      fd.append('file', file)
      return apiUpload<{ url: string }>('/api/staff-wiki/upload', fd)
    },
    onSuccess: (res) => {
      const url = res.data?.url
      if (!url) return
      setFormBody((b) => `${b.trimEnd()}\n\n![Скрин](${url})\n`)
      setFlash('Картинка загружена — вставлена в текст')
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка загрузки'),
  })

  const editor: ReactNode = (
    <div className="form-stack wiki-editor">
      <div className="field-grid">
        <label>
          Заголовок
          <input value={formTitle} onChange={(e) => setFormTitle(e.target.value)} />
        </label>
        <label>
          Slug (латиница)
          <input
            value={formSlug}
            onChange={(e) => setFormSlug(e.target.value)}
            placeholder="shift-case"
          />
        </label>
        <label>
          Порядок
          <input
            type="number"
            value={formSort}
            onChange={(e) => setFormSort(Number(e.target.value) || 0)}
          />
        </label>
      </div>
      <div className="wiki-editor-toolbar">
        <button type="button" className="ghost" onClick={() => fileRef.current?.click()} disabled={uploadMutation.isPending}>
          {uploadMutation.isPending ? 'Загрузка…' : 'Вставить скрин'}
        </button>
        <input
          ref={fileRef}
          type="file"
          accept="image/*"
          hidden
          onChange={(e) => {
            const f = e.target.files?.[0]
            if (f) uploadMutation.mutate(f)
            e.target.value = ''
          }}
        />
      </div>
      <label>
        Текст (Markdown)
        <textarea
          className="wiki-textarea"
          rows={16}
          value={formBody}
          onChange={(e) => setFormBody(e.target.value)}
        />
      </label>
      <div>
        <p className="muted" style={{ margin: '0 0 8px', fontSize: 13 }}>
          Превью
        </p>
        <WikiHtml markdown={formBody} />
      </div>
      {error && <p className="error">{error}</p>}
    </div>
  )

  return (
    <div className="wiki-layout">
      <aside className="wiki-toc panel">
        <div className="wiki-toc-head">
          <h2>Инструкция</h2>
          {owner && (
            <button type="button" className="ghost" onClick={openCreate}>
              + Статья
            </button>
          )}
        </div>
        <p className="muted wiki-toc-hint">Для сотрудников кассы. Владелец может править текст и скрины.</p>
        <nav className="wiki-toc-nav">
          {pages.map((p) => (
            <Link
              key={p.id}
              to={`/wiki/${p.slug}`}
              className={p.slug === activeSlug ? 'is-active' : undefined}
            >
              {p.title}
            </Link>
          ))}
          {pages.length === 0 && !listQuery.isLoading && <span className="muted">Пока пусто</span>}
        </nav>
      </aside>

      <article className="wiki-article panel">
        {flash && <p className="flash">{flash}</p>}
        {pageQuery.isLoading && <p className="muted">Загрузка…</p>}
        {pageQuery.isError && <p className="error">{(pageQuery.error as Error).message}</p>}
        {page && (
          <>
            <header className="wiki-article-head">
              <div>
                <h1>{page.title}</h1>
                {page.updatedAt && (
                  <p className="muted" style={{ margin: 0, fontSize: 13 }}>
                    Обновлено {new Date(page.updatedAt).toLocaleString('ru-RU')}
                  </p>
                )}
              </div>
              {owner && (
                <div className="wiki-article-actions">
                  <button type="button" onClick={openEdit}>
                    Редактировать
                  </button>
                  <button type="button" className="ghost danger-text" onClick={() => setDeleteOpen(true)}>
                    Удалить
                  </button>
                </div>
              )}
            </header>
            <WikiHtml markdown={page.bodyMarkdown} />
          </>
        )}
      </article>

      <ActionDialog
        open={editOpen}
        onClose={() => !saveMutation.isPending && setEditOpen(false)}
        title="Редактировать статью"
        size="xl"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setEditOpen(false)} disabled={saveMutation.isPending}>
              Отмена
            </button>
            <button type="button" disabled={saveMutation.isPending} onClick={() => saveMutation.mutate('update')}>
              {saveMutation.isPending ? 'Сохранение…' : 'Сохранить'}
            </button>
          </>
        }
      >
        {editor}
      </ActionDialog>

      <ActionDialog
        open={createOpen}
        onClose={() => !saveMutation.isPending && setCreateOpen(false)}
        title="Новая статья"
        size="xl"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setCreateOpen(false)} disabled={saveMutation.isPending}>
              Отмена
            </button>
            <button type="button" disabled={saveMutation.isPending} onClick={() => saveMutation.mutate('create')}>
              {saveMutation.isPending ? 'Создание…' : 'Создать'}
            </button>
          </>
        }
      >
        {editor}
      </ActionDialog>

      <ConfirmDialog
        open={deleteOpen}
        onClose={() => setDeleteOpen(false)}
        title="Удалить статью?"
        message={page ? `«${page.title}» будет удалена.` : ''}
        confirmLabel="Удалить"
        danger
        loading={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutate()}
      />
    </div>
  )
}
