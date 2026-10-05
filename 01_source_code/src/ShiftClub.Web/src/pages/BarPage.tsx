import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiUpload, getToken, newIdempotencyKey } from '../api/client'
import * as signalR from '@microsoft/signalr'
import { ActionDialog } from '../components/ActionDialog'
import { BarOrderBoard, type BarOrderPaidWith } from '../components/BarOrderBoard'
import { BarPosPanel, type BarReceiptDto } from '../components/BarPosPanel'
import { type BarPayMethod } from '../components/BarPaymentPick'
import { payKaspiTerminalIfReady } from '../kaspiPos'
import { money } from '../format'
import { can, Perm } from '../permissions'

type ProductDto = {
  id: string
  categoryId: string
  categoryName: string
  name: string
  sku: string
  barcode?: string | null
  salePrice: number
  costPrice: number
  stockQty: number
  minStockQty: number
  isLowStock: boolean
  isActive: boolean
  unit: string
  imageUrl?: string | null
}

type CategoryDto = { id: string; name: string; code: string; sortOrder: number; isActive: boolean }

type BarOrderDto = {
  id: string
  number: string
  status: string | number
  paymentMode: string | number
  computerName?: string
  total: number
  createdAt: string
  items: { productName: string; quantity: number; lineTotal: number }[]
}

function orderStatus(status: string | number): string {
  if (typeof status === 'string') return status
  const map = ['New', 'Accepted', 'Preparing', 'Ready', 'Delivering', 'Completed', 'Cancelled', 'Rejected']
  return map[status] ?? String(status)
}

type CartItem = { productId: string; name: string; price: number; qty: number; imageUrl?: string | null }
type BarTab = 'sell' | 'orders' | 'catalog'

type CashShiftDto = {
  id: string
  number: string
  status: string | number
  revenueNow: number
}

type ProductForm = {
  categoryId: string
  name: string
  sku: string
  barcode: string
  unit: string
  salePrice: string
  costPrice: string
  initialStock: string
  minStockQty: string
  isActive: boolean
}

const emptyProductForm = (categoryId = ''): ProductForm => ({
  categoryId,
  name: '',
  sku: '',
  barcode: '',
  unit: 'шт',
  salePrice: '',
  costPrice: '0',
  initialStock: '0',
  minStockQty: '5',
  isActive: true,
})

function mediaUrl(path?: string | null) {
  if (!path) return null
  if (path.startsWith('http://') || path.startsWith('https://')) return path
  return path
}

function defaultTab(): BarTab {
  if (can(Perm.BarSell)) return 'sell'
  if (can(Perm.BarOrders)) return 'orders'
  if (can(Perm.InventoryManage)) return 'catalog'
  return 'sell'
}

export function BarPage() {
  const queryClient = useQueryClient()
  const canView = can(Perm.BarView, Perm.BarSell, Perm.BarOrders, Perm.InventoryManage)
  const canSell = can(Perm.BarSell)
  const canOrders = can(Perm.BarOrders)
  const canInventory = can(Perm.InventoryManage)

  const [cart, setCart] = useState<CartItem[]>([])
  const [paymentMethod, setPaymentMethod] = useState<BarPayMethod | null>(null)
  const [mixedCash, setMixedCash] = useState('')
  const [lastReceipt, setLastReceipt] = useState<BarReceiptDto | null>(null)
  const [statusBusyId, setStatusBusyId] = useState<string | null>(null)
  const [kaspiPaying, setKaspiPaying] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)
  const [tab, setTab] = useState<BarTab>(defaultTab)
  const [sellSearch, setSellSearch] = useState('')
  const [categoryFilter, setCategoryFilter] = useState('')
  const [catalogFilter, setCatalogFilter] = useState('')
  const [catalogSearch, setCatalogSearch] = useState('')
  const [showInactive, setShowInactive] = useState(false)
  const [lowStockOnly, setLowStockOnly] = useState(false)
  const [productForm, setProductForm] = useState<ProductForm>(emptyProductForm())
  const [stockDelta, setStockDelta] = useState<Record<string, string>>({})
  const [productOpen, setProductOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [pendingImage, setPendingImage] = useState<File | null>(null)
  const [categoryOpen, setCategoryOpen] = useState(false)
  const [editingCategoryId, setEditingCategoryId] = useState<string | null>(null)
  const [categoryForm, setCategoryForm] = useState({ name: '', code: '', sortOrder: '0' })

  useEffect(() => {
    if (tab === 'sell' && !canSell) setTab(defaultTab())
    if (tab === 'orders' && !canOrders) setTab(defaultTab())
    if (tab === 'catalog' && !canInventory) setTab(defaultTab())
  }, [tab, canSell, canOrders, canInventory])

  const productsQuery = useQuery({
    queryKey: ['bar-products', showInactive, tab, canInventory],
    queryFn: async () => {
      const q = tab === 'catalog' || showInactive ? '?includeInactive=true' : ''
      return (await apiFetch<ProductDto[]>(`/api/bar/products${q}`)).data ?? []
    },
    enabled: canView,
  })

  const categoriesQuery = useQuery({
    queryKey: ['bar-categories', tab],
    queryFn: async () => {
      const q = tab === 'catalog' ? '?includeInactive=true' : ''
      return (await apiFetch<CategoryDto[]>(`/api/bar/categories${q}`)).data ?? []
    },
    enabled: canView,
  })

  const ordersQuery = useQuery({
    queryKey: ['bar-orders'],
    queryFn: async () => (await apiFetch<BarOrderDto[]>('/api/bar/orders')).data ?? [],
    refetchInterval: 7000,
    enabled: canView && (canOrders || canSell),
  })

  const shiftQuery = useQuery({
    queryKey: ['cash-shift-current'],
    queryFn: async () => {
      try {
        return (await apiFetch<CashShiftDto>('/api/cash/shifts/current')).data ?? null
      } catch {
        return null
      }
    },
    refetchInterval: 15000,
    enabled: canSell,
  })

  const shiftReceiptsQuery = useQuery({
    queryKey: ['cash-receipts', shiftQuery.data?.id],
    enabled: !!shiftQuery.data?.id && canSell,
    queryFn: async () =>
      (await apiFetch<BarReceiptDto[]>(`/api/cash/shifts/${shiftQuery.data!.id}/receipts`)).data ?? [],
    refetchInterval: 12000,
  })

  useEffect(() => {
    const cats = (categoriesQuery.data ?? []).filter((c) => c.isActive)
    if (cats.length && !productForm.categoryId) {
      setProductForm((f) => ({ ...f, categoryId: cats[0].id }))
    }
  }, [categoriesQuery.data, productForm.categoryId])

  useEffect(() => {
    if (!canOrders && !canSell) return
    const token = getToken()
    if (!token) return
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/staff?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect()
      .build()

    const refresh = () => void queryClient.invalidateQueries({ queryKey: ['bar-orders'] })
    connection.on('OrderCreated', () => {
      setFlash('Новый заказ с ПК')
      if (canOrders) setTab('orders')
      refresh()
    })
    connection.on('OrderStatusChanged', refresh)
    void connection.start()
    return () => {
      void connection.stop()
    }
  }, [queryClient, canOrders, canSell])

  const sellProducts = useMemo(() => {
    let list = (productsQuery.data ?? []).filter((p) => p.isActive)
    if (categoryFilter) list = list.filter((p) => p.categoryId === categoryFilter)
    const needle = sellSearch.trim().toLowerCase()
    if (needle) {
      list = list.filter(
        (p) =>
          p.name.toLowerCase().includes(needle) ||
          p.categoryName.toLowerCase().includes(needle) ||
          (p.barcode?.toLowerCase().includes(needle) ?? false) ||
          p.sku.toLowerCase().includes(needle),
      )
    }
    return list
  }, [productsQuery.data, categoryFilter, sellSearch])

  const catalogProducts = useMemo(() => {
    let list = productsQuery.data ?? []
    if (!showInactive) list = list.filter((p) => p.isActive)
    if (lowStockOnly) list = list.filter((p) => p.isLowStock)
    if (catalogFilter) list = list.filter((p) => p.categoryId === catalogFilter)
    const needle = catalogSearch.trim().toLowerCase()
    if (needle) {
      list = list.filter(
        (p) =>
          p.name.toLowerCase().includes(needle) ||
          p.categoryName.toLowerCase().includes(needle),
      )
    }
    return list
  }, [productsQuery.data, showInactive, lowStockOnly, catalogFilter, catalogSearch])

  const activeCategories = useMemo(
    () => (categoriesQuery.data ?? []).filter((c) => c.isActive),
    [categoriesQuery.data],
  )

  const cartTotal = useMemo(() => cart.reduce((s, i) => s + i.price * i.qty, 0), [cart])
  const cartQtyByProduct = useMemo(() => {
    const map = new Map<string, number>()
    for (const c of cart) map.set(c.productId, c.qty)
    return map
  }, [cart])

  const openOrders = useMemo(() => {
    return (ordersQuery.data ?? []).filter((o) => {
      const st = orderStatus(o.status)
      return st === 'New' || st === 'Accepted' || st === 'Preparing' || st === 'Ready' || st === 'Delivering'
    })
  }, [ordersQuery.data])

  const lowStockCount = useMemo(
    () => (productsQuery.data ?? []).filter((p) => p.isLowStock && p.isActive).length,
    [productsQuery.data],
  )

  function addToCart(p: ProductDto) {
    if (p.stockQty <= 0) {
      setError(`«${p.name}» нет на складе`)
      return
    }
    setError(null)
    setCart((prev) => {
      const existing = prev.find((x) => x.productId === p.id)
      if (existing) {
        if (existing.qty >= p.stockQty) {
          setError(`На складе только ${p.stockQty} шт.`)
          return prev
        }
        return prev.map((x) => (x.productId === p.id ? { ...x, qty: x.qty + 1 } : x))
      }
      return [...prev, { productId: p.id, name: p.name, price: p.salePrice, qty: 1, imageUrl: p.imageUrl }]
    })
  }

  function changeQty(productId: string, delta: number) {
    setCart((prev) =>
      prev
        .map((x) => (x.productId === productId ? { ...x, qty: x.qty + delta } : x))
        .filter((x) => x.qty > 0),
    )
  }

  function openCreateProduct() {
    const cat = activeCategories[0]?.id ?? ''
    setEditingId(null)
    setProductForm(emptyProductForm(cat))
    setPendingImage(null)
    setError(null)
    setProductOpen(true)
  }

  function openEditProduct(p: ProductDto) {
    setEditingId(p.id)
    setProductForm({
      categoryId: p.categoryId,
      name: p.name,
      sku: p.sku,
      barcode: p.barcode ?? '',
      unit: p.unit || 'шт',
      salePrice: String(p.salePrice),
      costPrice: String(p.costPrice),
      initialStock: String(p.stockQty),
      minStockQty: String(p.minStockQty),
      isActive: p.isActive,
    })
    setPendingImage(null)
    setError(null)
    setProductOpen(true)
  }

  function openCreateCategory() {
    setEditingCategoryId(null)
    setCategoryForm({ name: '', code: '', sortOrder: '0' })
    setError(null)
    setCategoryOpen(true)
  }

  function openEditCategory(c: CategoryDto) {
    setEditingCategoryId(c.id)
    setCategoryForm({ name: c.name, code: c.code, sortOrder: String(c.sortOrder) })
    setError(null)
    setCategoryOpen(true)
  }

  const sellMutation = useMutation({
    mutationFn: async () => {
      if (!paymentMethod) throw new Error('Выберите способ оплаты')
      const payments =
        paymentMethod === 'Mixed'
          ? (() => {
              const cash = Math.round(Number(mixedCash) || 0)
              const kaspi = Math.round(cartTotal) - cash
              if (cash <= 0 || kaspi <= 0)
                throw new Error('Смешанная: нал и Kaspi должны быть больше 0')
              return [
                { method: 'Cash', amount: cash },
                { method: 'KaspiQr', amount: kaspi },
              ]
            })()
          : undefined
      const kaspiAmount =
        paymentMethod === 'KaspiQr'
          ? cartTotal
          : paymentMethod === 'Mixed'
            ? (payments?.find((p) => p.method === 'KaspiQr')?.amount ?? 0)
            : 0
      if (kaspiAmount > 0) {
        setKaspiPaying(true)
        try {
          await payKaspiTerminalIfReady(kaspiAmount)
        } finally {
          setKaspiPaying(false)
        }
      }
      const res = await apiFetch<BarReceiptDto>('/api/bar/sell', {
        method: 'POST',
        body: JSON.stringify({
          items: cart.map((c) => ({ productId: c.productId, quantity: c.qty })),
          paymentMethod,
          payments,
          comment: 'Продажа с панели бара',
          idempotencyKey: newIdempotencyKey(),
        }),
      })
      if (!res.data) throw new Error('Не удалось пробить чек')
      return res.data
    },
    onSuccess: async (receipt) => {
      setError(null)
      setCart([])
      setPaymentMethod(null)
      setMixedCash('')
      setLastReceipt(receipt)
      setFlash(`Продано · чек ${receipt.number}`)
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
      await queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
      await queryClient.invalidateQueries({ queryKey: ['cash-receipts'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка продажи'),
  })

  const statusMutation = useMutation({
    mutationFn: async (payload: { id: string; status: string; paidWith?: BarOrderPaidWith; total?: number }) => {
      setStatusBusyId(payload.id)
      if (payload.paidWith === 'KaspiQr' && payload.total && payload.total > 0) {
        setKaspiPaying(true)
        try {
          await payKaspiTerminalIfReady(payload.total)
        } finally {
          setKaspiPaying(false)
        }
      }
      return apiFetch(`/api/bar/orders/${payload.id}/status`, {
        method: 'POST',
        body: JSON.stringify({ status: payload.status, paymentMethod: payload.paidWith }),
      })
    },
    onSuccess: async (_, vars) => {
      setStatusBusyId(null)
      const done = vars.status === 'Completed'
      setFlash(done ? 'Заказ выдан' : 'Статус обновлён')
      await queryClient.invalidateQueries({ queryKey: ['bar-orders'] })
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
      if (done) {
        await queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
        await queryClient.invalidateQueries({ queryKey: ['cash-receipts'] })
      }
    },
    onError: (e) => {
      setStatusBusyId(null)
      setError(e instanceof Error ? e.message : 'Ошибка статуса')
    },
  })

  const saveProductMutation = useMutation({
    mutationFn: async () => {
      if (editingId) {
        const updated = await apiFetch<ProductDto>(`/api/bar/products/${editingId}`, {
          method: 'PUT',
          body: JSON.stringify({
            categoryId: productForm.categoryId,
            name: productForm.name.trim(),
            sku: productForm.sku.trim() || undefined,
            barcode: productForm.barcode.trim() || null,
            unit: productForm.unit.trim() || 'шт',
            costPrice: Number(productForm.costPrice) || 0,
            salePrice: Number(productForm.salePrice) || 0,
            minStockQty: Number(productForm.minStockQty) || 0,
            isActive: productForm.isActive,
          }),
        })
        if (pendingImage && updated.data) {
          const form = new FormData()
          form.append('file', pendingImage)
          await apiUpload<ProductDto>(`/api/bar/products/${updated.data.id}/image`, form)
        }
        return updated
      }

      const created = await apiFetch<ProductDto>('/api/bar/products', {
        method: 'POST',
        body: JSON.stringify({
          categoryId: productForm.categoryId,
          name: productForm.name.trim(),
          sku: productForm.sku.trim() || `SKU-${Date.now()}`,
          barcode: productForm.barcode.trim() || null,
          unit: productForm.unit.trim() || 'шт',
          costPrice: Number(productForm.costPrice) || 0,
          salePrice: Number(productForm.salePrice) || 0,
          initialStock: Number(productForm.initialStock) || 0,
          minStockQty: Number(productForm.minStockQty) || 0,
        }),
      })
      const product = created.data
      if (product && pendingImage) {
        const form = new FormData()
        form.append('file', pendingImage)
        await apiUpload<ProductDto>(`/api/bar/products/${product.id}/image`, form)
      }
      return created
    },
    onSuccess: async () => {
      setError(null)
      setFlash(editingId ? 'Товар сохранён' : 'Товар добавлен')
      setProductOpen(false)
      setEditingId(null)
      setPendingImage(null)
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка сохранения товара'),
  })

  const deleteProductMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/bar/products/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setFlash('Товар удалён (или скрыт, если есть продажи)')
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка удаления'),
  })

  const toggleActiveMutation = useMutation({
    mutationFn: async (payload: { id: string; isActive: boolean }) =>
      apiFetch(`/api/bar/products/${payload.id}`, {
        method: 'PUT',
        body: JSON.stringify({ isActive: payload.isActive }),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка статуса товара'),
  })

  const clearImageMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/bar/products/${id}/image`, { method: 'DELETE' }),
    onSuccess: async () => {
      setFlash('Фото удалено')
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка удаления фото'),
  })

  const stockMutation = useMutation({
    mutationFn: async (payload: { id: string; delta: number }) =>
      apiFetch(`/api/bar/products/${payload.id}/stock`, {
        method: 'POST',
        body: JSON.stringify({
          quantityDelta: payload.delta,
          type: 'Adjustment',
          comment: 'Корректировка с панели',
          idempotencyKey: newIdempotencyKey(),
        }),
      }),
    onSuccess: async (_, vars) => {
      setStockDelta((s) => ({ ...s, [vars.id]: '' }))
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка остатка'),
  })

  const imageMutation = useMutation({
    mutationFn: async (payload: { id: string; file: File }) => {
      const form = new FormData()
      form.append('file', payload.file)
      return apiUpload<ProductDto>(`/api/bar/products/${payload.id}/image`, form)
    },
    onSuccess: async () => {
      setFlash('Фото обновлено')
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка загрузки фото'),
  })

  const saveCategoryMutation = useMutation({
    mutationFn: async () => {
      if (editingCategoryId) {
        return apiFetch(`/api/bar/categories/${editingCategoryId}`, {
          method: 'PUT',
          body: JSON.stringify({
            name: categoryForm.name.trim(),
            code: categoryForm.code.trim() || undefined,
            sortOrder: Number(categoryForm.sortOrder) || 0,
          }),
        })
      }
      return apiFetch('/api/bar/categories', {
        method: 'POST',
        body: JSON.stringify({
          name: categoryForm.name.trim(),
          code: categoryForm.code.trim() || null,
          sortOrder: Number(categoryForm.sortOrder) || 0,
        }),
      })
    },
    onSuccess: async () => {
      setFlash(editingCategoryId ? 'Категория сохранена' : 'Категория добавлена')
      setCategoryOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['bar-categories'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка категории'),
  })

  const deleteCategoryMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/bar/categories/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setFlash('Категория удалена или скрыта')
      await queryClient.invalidateQueries({ queryKey: ['bar-categories'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка удаления категории'),
  })

  const toggleCategoryMutation = useMutation({
    mutationFn: async (payload: { id: string; isActive: boolean }) =>
      apiFetch(`/api/bar/categories/${payload.id}`, {
        method: 'PUT',
        body: JSON.stringify({ isActive: payload.isActive }),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['bar-categories'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка статуса категории'),
  })

  if (!canView) {
    return (
      <div className="page-head">
        <div>
          <h1>Бар</h1>
          <p className="muted">Нет доступа. Нужно право bar.view.</p>
          <Link to="/">На главную</Link>
        </div>
      </div>
    )
  }

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Бар</h1>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            {canSell && 'Касса бара — товар → чек → оплата'}
            {canSell && canOrders && ' · '}
            {canOrders && 'заказы с ПК отдельно'}
            {(canSell || canOrders) && canInventory && ' · '}
            {canInventory && 'каталог и склад'}
          </p>
        </div>
        <div className="bar-tabs order-actions">
          {canSell && (
            <button type="button" className={tab === 'sell' ? undefined : 'ghost'} onClick={() => setTab('sell')}>
              Продажа
            </button>
          )}
          {canOrders && (
            <button type="button" className={tab === 'orders' ? undefined : 'ghost'} onClick={() => setTab('orders')}>
              Заказы
              {openOrders.length > 0 && <span className="bar-tab-badge">{openOrders.length}</span>}
            </button>
          )}
          {canInventory && (
            <button
              type="button"
              className={tab === 'catalog' ? undefined : 'ghost'}
              onClick={() => setTab('catalog')}
            >
              Товары
              {lowStockCount > 0 && <span className="bar-tab-badge is-warn">{lowStockCount}</span>}
            </button>
          )}
        </div>
      </header>

      {productsQuery.isError && <p className="error">{(productsQuery.error as Error).message}</p>}
      {flash && (
        <p className="flash" onAnimationEnd={() => setFlash(null)}>
          {flash}
        </p>
      )}
      {error && <p className="error">{error}</p>}

      {tab !== 'catalog' && (
        <div className="ops-stats">
          {canOrders && (
            <button
              type="button"
              className="ops-stat ops-stat--btn"
              style={{ ['--stat-accent' as string]: openOrders.length ? '#ff8a2b' : '#3ddc97' }}
              onClick={() => setTab('orders')}
            >
              <span>Заказы с ПК</span>
              <strong>{openOrders.length}</strong>
            </button>
          )}
          {canSell && tab === 'sell' && (
            <div className="ops-stat" style={{ ['--stat-accent' as string]: '#60a5fa' }}>
              <span>В чеке</span>
              <strong>
                {cart.length ? `${cart.reduce((s, c) => s + c.qty, 0)} шт · ${money(cartTotal)} ₸` : 'пусто'}
              </strong>
            </div>
          )}
          {canInventory && tab !== 'sell' && (
            <button
              type="button"
              className="ops-stat ops-stat--btn"
              style={{ ['--stat-accent' as string]: lowStockCount ? '#fbbf24' : '#64748b' }}
              onClick={() => {
                setTab('catalog')
                setLowStockOnly(true)
              }}
            >
              <span>Мало на складе</span>
              <strong>{lowStockCount}</strong>
            </button>
          )}
        </div>
      )}

      {tab === 'sell' && canSell && (
        <div className="bar-pos">
          <section className="cash-card bar-pos-catalog">
            <div className="bar-pos-toolbar">
              <input
                className="bar-pos-search"
                value={sellSearch}
                onChange={(e) => setSellSearch(e.target.value)}
                placeholder="Поиск товара или штрихкода…"
                autoFocus
              />
              <div className="order-actions bar-cat-chips">
                <button
                  type="button"
                  className={!categoryFilter ? undefined : 'ghost'}
                  onClick={() => setCategoryFilter('')}
                >
                  Все
                </button>
                {activeCategories.map((c) => (
                  <button
                    key={c.id}
                    type="button"
                    className={categoryFilter === c.id ? undefined : 'ghost'}
                    onClick={() => setCategoryFilter(c.id)}
                  >
                    {c.name}
                  </button>
                ))}
              </div>
            </div>
            <div className="product-grid product-grid--pos">
              {sellProducts.map((p) => {
                const img = mediaUrl(p.imageUrl)
                const out = p.stockQty <= 0
                const inCart = cartQtyByProduct.get(p.id) ?? 0
                return (
                  <button
                    key={p.id}
                    type="button"
                    className={`product-tile${out ? ' is-out' : ''}${p.isLowStock ? ' is-low' : ''}${inCart ? ' is-in-cart' : ''}`}
                    onClick={() => addToCart(p)}
                    disabled={out}
                  >
                    {inCart > 0 && <span className="product-tile-qty">{inCart}</span>}
                    <div className="product-tile-media">
                      {img ? (
                        <img src={img} alt="" />
                      ) : (
                        <span className="product-tile-letter">{p.name.slice(0, 1).toUpperCase()}</span>
                      )}
                    </div>
                    <strong>{p.name}</strong>
                    <span className="product-tile-price">{money(p.salePrice)} ₸</span>
                    {!out && p.stockQty <= p.minStockQty && (
                      <span className="product-tile-stock muted">ост. {p.stockQty}</span>
                    )}
                    {out && <span className="error">Нет в наличии</span>}
                  </button>
                )
              })}
              {sellProducts.length === 0 && <p className="muted">Ничего не найдено</p>}
            </div>
          </section>

          <BarPosPanel
            cart={cart}
            cartTotal={cartTotal}
            paymentMethod={paymentMethod}
            onPaymentChange={(m) => {
              setPaymentMethod(m)
              if (m === 'Mixed' && !mixedCash)
                setMixedCash(String(Math.round(cartTotal / 2) || ''))
            }}
            mixedCash={mixedCash}
            onMixedCashChange={setMixedCash}
            onQtyChange={changeQty}
            onClear={() => setCart([])}
            onPay={() => sellMutation.mutate()}
            paying={sellMutation.isPending || kaspiPaying}
            kaspiWaiting={kaspiPaying}
            shift={shiftQuery.data}
            shiftLoading={shiftQuery.isLoading}
            lastReceipt={lastReceipt}
            onDismissReceipt={() => setLastReceipt(null)}
            recentReceipts={shiftReceiptsQuery.data ?? []}
          />
        </div>
      )}

      {tab === 'orders' && canOrders && (
        <BarOrderBoard
          orders={openOrders}
          busyId={statusBusyId}
          onStatus={(id, status, paidWith) =>
            statusMutation.mutate({
              id,
              status,
              paidWith,
              total: openOrders.find((o) => o.id === id)?.total,
            })
          }
        />
      )}

      {tab === 'catalog' && canInventory && (
        <div className="list-page">
          <section className="list-card" style={{ marginBottom: 16 }}>
            <div className="page-toolbar">
              <div className="page-toolbar-left">
                <h2 className="section-title" style={{ margin: 0 }}>
                  Категории
                </h2>
              </div>
              <div className="page-toolbar-right">
                <button type="button" onClick={openCreateCategory}>
                  + Категория
                </button>
              </div>
            </div>
            <ul className="receipt-list">
              {(categoriesQuery.data ?? []).map((c) => (
                <li key={c.id}>
                  <div className="catalog-row">
                    <div className="catalog-row-main">
                      <strong>
                        {c.name}
                        {!c.isActive && <span className="muted"> · скрыта</span>}
                      </strong>
                      <div className="muted">
                        {c.code} · порядок {c.sortOrder}
                      </div>
                    </div>
                    <div className="order-actions">
                      <button type="button" className="ghost" onClick={() => openEditCategory(c)}>
                        Изменить
                      </button>
                      <button
                        type="button"
                        className="ghost"
                        onClick={() => toggleCategoryMutation.mutate({ id: c.id, isActive: !c.isActive })}
                      >
                        {c.isActive ? 'Скрыть' : 'Показать'}
                      </button>
                      <button
                        type="button"
                        className="ghost"
                        onClick={() => {
                          if (window.confirm(`Удалить категорию «${c.name}»?`)) {
                            deleteCategoryMutation.mutate(c.id)
                          }
                        }}
                      >
                        Удалить
                      </button>
                    </div>
                  </div>
                </li>
              ))}
              {(categoriesQuery.data?.length ?? 0) === 0 && <li className="muted">Нет категорий</li>}
            </ul>
          </section>

          <div className="page-toolbar">
            <div className="page-toolbar-left">
              <h2 className="section-title" style={{ margin: 0 }}>
                Товары / склад
              </h2>
            </div>
            <div className="page-toolbar-right order-actions">
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
                <input
                  type="checkbox"
                  checked={lowStockOnly}
                  onChange={(e) => setLowStockOnly(e.target.checked)}
                />
                Только мало
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}>
                <input
                  type="checkbox"
                  checked={showInactive}
                  onChange={(e) => setShowInactive(e.target.checked)}
                />
                Скрытые
              </label>
              <button type="button" onClick={openCreateProduct}>
                + Новый товар
              </button>
            </div>
          </div>

          <div className="bar-pos-toolbar" style={{ marginBottom: 12 }}>
            <input
              className="bar-pos-search"
              value={catalogSearch}
              onChange={(e) => setCatalogSearch(e.target.value)}
              placeholder="Поиск товара…"
            />
            <div className="order-actions bar-cat-chips">
              <button
                type="button"
                className={!catalogFilter ? undefined : 'ghost'}
                onClick={() => setCatalogFilter('')}
              >
                Все
              </button>
              {activeCategories.map((c) => (
                <button
                  key={c.id}
                  type="button"
                  className={catalogFilter === c.id ? undefined : 'ghost'}
                  onClick={() => setCatalogFilter(c.id)}
                >
                  {c.name}
                </button>
              ))}
            </div>
          </div>

          <section className="list-card">
            <ul className="receipt-list">
              {catalogProducts.map((p) => {
                const img = mediaUrl(p.imageUrl)
                return (
                  <li key={p.id}>
                    <div className="catalog-row">
                      {img ? (
                        <img className="catalog-thumb" src={img} alt="" />
                      ) : (
                        <div
                          className="catalog-thumb product-tile-letter"
                          style={{ display: 'grid', placeItems: 'center' }}
                        >
                          {p.name.slice(0, 1).toUpperCase()}
                        </div>
                      )}
                      <div className="catalog-row-main">
                        <strong>
                          {p.name}
                          {!p.isActive && <span className="muted"> · скрыт</span>}
                        </strong>
                        <div className="muted">
                          {p.categoryName} · {p.salePrice.toLocaleString('ru-RU')} ₸
                        </div>
                        <div className={p.isLowStock ? 'error' : 'muted'}>
                          Остаток: {p.stockQty} шт. · себест. {p.costPrice} ₸
                        </div>
                      </div>
                      <div
                        className="order-actions"
                        style={{ flexWrap: 'wrap', maxWidth: 420, justifyContent: 'flex-end' }}
                      >
                        <button type="button" className="ghost" onClick={() => openEditProduct(p)}>
                          Изменить
                        </button>
                        <label className="ghost" style={{ cursor: 'pointer', padding: '8px 12px' }}>
                          Фото
                          <input
                            type="file"
                            accept="image/png,image/jpeg,image/webp,image/gif"
                            hidden
                            onChange={(e) => {
                              const file = e.target.files?.[0]
                              if (file) imageMutation.mutate({ id: p.id, file })
                              e.target.value = ''
                            }}
                          />
                        </label>
                        {p.imageUrl && (
                          <button
                            type="button"
                            className="ghost"
                            onClick={() => clearImageMutation.mutate(p.id)}
                          >
                            Убрать фото
                          </button>
                        )}
                        <button
                          type="button"
                          className="ghost"
                          onClick={() =>
                            toggleActiveMutation.mutate({ id: p.id, isActive: !p.isActive })
                          }
                        >
                          {p.isActive ? 'Скрыть' : 'Показать'}
                        </button>
                        <button
                          type="button"
                          className="ghost"
                          onClick={() => {
                            if (
                              window.confirm(
                                `Удалить «${p.name}»? Если были продажи — товар только скроется.`,
                              )
                            ) {
                              deleteProductMutation.mutate(p.id)
                            }
                          }}
                        >
                          Удалить
                        </button>
                        <input
                          style={{ width: 72 }}
                          type="number"
                          placeholder="±"
                          value={stockDelta[p.id] ?? ''}
                          onChange={(e) => setStockDelta((s) => ({ ...s, [p.id]: e.target.value }))}
                        />
                        <button
                          type="button"
                          className="ghost"
                          disabled={!stockDelta[p.id] || stockMutation.isPending}
                          onClick={() =>
                            stockMutation.mutate({ id: p.id, delta: Number(stockDelta[p.id]) || 0 })
                          }
                        >
                          Остаток
                        </button>
                      </div>
                    </div>
                  </li>
                )
              })}
              {catalogProducts.length === 0 && <li className="muted">Нет товаров</li>}
            </ul>
          </section>

          <ActionDialog
            open={productOpen}
            onClose={() => !saveProductMutation.isPending && setProductOpen(false)}
            title={editingId ? 'Редактировать товар' : 'Новый товар'}
            size="md"
            footer={
              <>
                <button
                  type="button"
                  className="ghost"
                  onClick={() => setProductOpen(false)}
                  disabled={saveProductMutation.isPending}
                >
                  Отмена
                </button>
                <button
                  type="button"
                  disabled={!productForm.name || !productForm.categoryId || saveProductMutation.isPending}
                  onClick={() => saveProductMutation.mutate()}
                >
                  {editingId ? 'Сохранить' : 'Добавить'}
                </button>
              </>
            }
          >
            <div className="form-stack">
              <label>
                Категория
                <select
                  value={productForm.categoryId}
                  onChange={(e) => setProductForm({ ...productForm, categoryId: e.target.value })}
                >
                  {(categoriesQuery.data ?? [])
                    .filter((c) => c.isActive || c.id === productForm.categoryId)
                    .map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                        {!c.isActive ? ' (скрыта)' : ''}
                      </option>
                    ))}
                </select>
              </label>
              <label>
                Название
                <input
                  value={productForm.name}
                  onChange={(e) => setProductForm({ ...productForm, name: e.target.value })}
                />
              </label>
              <label>
                Фото
                <input
                  type="file"
                  accept="image/png,image/jpeg,image/webp,image/gif"
                  onChange={(e) => setPendingImage(e.target.files?.[0] ?? null)}
                />
                {pendingImage && <span className="muted">{pendingImage.name}</span>}
              </label>
              <div className="field-grid">
                <label>
                  Цена продажи
                  <input
                    type="number"
                    value={productForm.salePrice}
                    onChange={(e) => setProductForm({ ...productForm, salePrice: e.target.value })}
                  />
                </label>
                <label>
                  Себестоимость
                  <input
                    type="number"
                    value={productForm.costPrice}
                    onChange={(e) => setProductForm({ ...productForm, costPrice: e.target.value })}
                  />
                </label>
                {!editingId && (
                  <label>
                    Начальный остаток (шт.)
                    <input
                      type="number"
                      value={productForm.initialStock}
                      onChange={(e) =>
                        setProductForm({ ...productForm, initialStock: e.target.value })
                      }
                    />
                  </label>
                )}
              </div>
              {editingId && (
                <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <input
                    type="checkbox"
                    checked={productForm.isActive}
                    onChange={(e) => setProductForm({ ...productForm, isActive: e.target.checked })}
                  />
                  Активен (виден в продаже)
                </label>
              )}
            </div>
          </ActionDialog>

          <ActionDialog
            open={categoryOpen}
            onClose={() => !saveCategoryMutation.isPending && setCategoryOpen(false)}
            title={editingCategoryId ? 'Редактировать категорию' : 'Новая категория'}
            size="sm"
            footer={
              <>
                <button
                  type="button"
                  className="ghost"
                  onClick={() => setCategoryOpen(false)}
                  disabled={saveCategoryMutation.isPending}
                >
                  Отмена
                </button>
                <button
                  type="button"
                  disabled={!categoryForm.name.trim() || saveCategoryMutation.isPending}
                  onClick={() => saveCategoryMutation.mutate()}
                >
                  {editingCategoryId ? 'Сохранить' : 'Добавить'}
                </button>
              </>
            }
          >
            <div className="form-stack">
              <label>
                Название
                <input
                  value={categoryForm.name}
                  onChange={(e) => setCategoryForm({ ...categoryForm, name: e.target.value })}
                />
              </label>
              <label>
                Код (необязательно)
                <input
                  value={categoryForm.code}
                  onChange={(e) => setCategoryForm({ ...categoryForm, code: e.target.value })}
                  placeholder="авто из названия"
                />
              </label>
              <label>
                Порядок
                <input
                  type="number"
                  value={categoryForm.sortOrder}
                  onChange={(e) => setCategoryForm({ ...categoryForm, sortOrder: e.target.value })}
                />
              </label>
            </div>
          </ActionDialog>
        </div>
      )}
    </>
  )
}
