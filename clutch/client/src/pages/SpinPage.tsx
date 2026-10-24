import { useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api'
import { CARD, GAP, Reel } from '../components/Reel'
import { rarityLabel } from '../labels'
import type { ClubPublic, PublicCase, PublicPrize, SpinResult } from '../types'

export function SpinPage() {
  const { slug = '', caseSlug } = useParams()
  const [club, setClub] = useState<ClubPublic | null>(null)
  const [data, setData] = useState<PublicCase | null>(null)
  const [error, setError] = useState('')
  const [ticket, setTicket] = useState('')
  const [busy, setBusy] = useState(false)
  const [strip, setStrip] = useState<PublicPrize[]>([])
  const [offset, setOffset] = useState(0)
  const [animate, setAnimate] = useState(false)
  const [result, setResult] = useState<SpinResult | null>(null)
  const reelRef = useRef<HTMLDivElement>(null)
  const timers = useRef<number[]>([])

  useEffect(() => {
    let ignore = false
    setError('')
    setData(null)
    setResult(null)
    api<ClubPublic>(`/api/public/${slug}`)
      .then((next) => {
        if (ignore) return
        setClub(next)
        document.documentElement.style.setProperty('--club', next.accent)
        const only = next.cases[0]
        if (!caseSlug && only && next.cases.length === 1) {
          window.history.replaceState(null, '', `/c/${slug}/${only.slug}`)
        }
        const chosen = caseSlug || (next.cases.length === 1 ? only?.slug : '')
        if (!chosen) return
        return api<PublicCase>(`/api/public/${slug}/${chosen}`).then((payload) => {
          if (!ignore) {
            setData(payload)
            setStrip(payload.case.prizes.length ? Array.from({ length: 12 }, (_, i) => payload.case.prizes[i % payload.case.prizes.length]) : [])
          }
        })
      })
      .catch((err) => {
        if (!ignore) setError(err instanceof Error ? err.message : 'Кейс не открылся')
      })
    return () => {
      ignore = true
      timers.current.forEach((id) => window.clearTimeout(id))
    }
  }, [slug, caseSlug])

  async function openCase() {
    if (!data || busy) return
    setBusy(true)
    setError('')
    setResult(null)
    setAnimate(false)
    setOffset(0)
    try {
      const spin = await api<SpinResult>(`/api/public/${slug}/${data.case.slug}/spin`, {
        method: 'POST',
        body: JSON.stringify({ ticket }),
      })
      setStrip(spin.strip)
      const width = reelRef.current?.clientWidth ?? 360
      const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
      timers.current.push(window.setTimeout(() => {
        setAnimate(!reduced)
        setOffset(spin.landing * (CARD + GAP) - width / 2 + CARD / 2)
      }, 40))
      timers.current.push(window.setTimeout(() => {
        setResult(spin)
        setBusy(false)
      }, reduced ? 280 : 4900))
      tick()
    } catch (err) {
      setBusy(false)
      setError(err instanceof Error ? err.message : 'Не получилось открыть')
    }
  }

  const chosenMissing = club && club.cases.length > 1 && !data && !error

  return (
    <main className="spin-page">
      <header className="spin-top">
        <Link to="/" className="brand">CLUTCH</Link>
        <div>
          <strong>{data?.club.name || club?.name || 'Кейс'}</strong>
          <span>{data?.case.title || 'Выберите кейс'}</span>
        </div>
      </header>

      {error && !data && <p className="form-error center">{error}</p>}

      {chosenMissing && (
        <section className="case-pick">
          <h1>{club.name}</h1>
          <p>{club.tagline}</p>
          <div>
            {club.cases.map((item) => (
              <Link key={item.slug} to={`/c/${slug}/${item.slug}`}>
                <h2>{item.title}</h2>
                <p>{item.lead}</p>
              </Link>
            ))}
          </div>
        </section>
      )}

      {data && (
        <>
          <section className="spin-hero">
            <p className="eyebrow">{data.club.city}</p>
            <h1>{data.case.title}</h1>
            <p className="lead">{data.case.lead}</p>
          </section>
          <div ref={reelRef}>
            <Reel items={strip} offset={offset} animate={animate} />
          </div>
          <div className="spin-actions">
            {data.case.openMode === 'ticket' && (
              <label>
                Код от администратора
                <input value={ticket} onChange={(event) => setTicket(event.target.value.toUpperCase())} placeholder="ABC123" autoCapitalize="characters" />
              </label>
            )}
            <button className="btn btn-acid btn-xl" type="button" onClick={openCase} disabled={busy || data.case.prizes.length === 0}>
              {busy ? 'Крутится…' : 'Открыть кейс'}
            </button>
            {error && <p className="form-error">{error}</p>}
          </div>
          <section className="spin-prizes">
            {data.case.prizes.map((prize) => (
              <article key={prize.id} className={`rarity-${prize.rarity}`}>
                {prize.image && <img src={prize.image} alt="" />}
                <div>
                  <p>{rarityLabel[prize.rarity]}{prize.odds != null ? ` · ${prize.odds}%` : ''}</p>
                  <h3>{prize.title}</h3>
                  <span>{prize.detail}</span>
                </div>
              </article>
            ))}
          </section>
        </>
      )}

      {result && (
        <div className="result" role="dialog" aria-modal="true" aria-label="Приз">
          <div className={`result-card rarity-${result.prize.rarity}`}>
            {result.prize.image && <img src={result.prize.image} alt="" />}
            <p className="eyebrow">{rarityLabel[result.prize.rarity]}</p>
            <h2>{result.prize.title}</h2>
            <p>{result.prize.detail}</p>
            <p className="result-code">{result.code}</p>
            <p className="result-note">Назовите код администратору. Он зачислит приз в программе клуба.</p>
            <button className="btn btn-ghost" type="button" onClick={() => setResult(null)}>Закрыть</button>
          </div>
        </div>
      )}
    </main>
  )
}

function tick() {
  const AudioCtx = window.AudioContext || (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
  if (!AudioCtx) return
  const ctx = new AudioCtx()
  let step = 0
  const id = window.setInterval(() => {
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.frequency.value = 180 + step * 18
    gain.gain.value = 0.03
    osc.connect(gain)
    gain.connect(ctx.destination)
    osc.start()
    osc.stop(ctx.currentTime + 0.04)
    step += 1
    if (step > 18) {
      window.clearInterval(id)
      ctx.close().catch(() => undefined)
    }
  }, 140)
}
