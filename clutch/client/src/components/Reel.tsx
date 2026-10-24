import type { PublicPrize } from '../types'
import { rarityLabel } from '../labels'

export const CARD = 168
export const GAP = 12

export function Reel({
  items,
  offset,
  animate,
}: {
  items: PublicPrize[]
  offset: number
  animate: boolean
}) {
  return (
    <div className="reel">
      <div className="reel-line" aria-hidden="true" />
      <div
        className={animate ? 'reel-track is-on' : 'reel-track'}
        style={{ transform: `translate3d(${-offset}px, 0, 0)` }}
      >
        {items.map((prize, index) => (
          <article className={`loot rarity-${prize.rarity}`} key={`${prize.id}-${index}`}>
            <div className="loot-art">
              {prize.image ? <img src={prize.image} alt="" /> : <span>{prize.title.slice(0, 1)}</span>}
            </div>
            <p className="loot-rarity">{rarityLabel[prize.rarity]}</p>
            <h3>{prize.title}</h3>
            {prize.odds != null && <p className="loot-odds">{prize.odds}%</p>}
          </article>
        ))}
      </div>
    </div>
  )
}
