import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { AdminApi } from '../../core/api/api';
import { AdminCategory, AdminProduct, InventoryItem, InventoryReason, InventoryTransaction, PagedResult, SpiceLevel } from '../../core/api/models';
import { ToastService, fieldErrors } from '../../core/state/toast';
import { Icon } from '../../shared/icon';
import { EmptyState, InrPipe, SmartImage, VegMark, utc } from '../../shared/ui';
import { ADMIN_PAGE } from './admin-layout';

/* ============================== Menu items ============================== */

@Component({
  selector: 'bb-admin-products',
  imports: [FormsModule, ReactiveFormsModule, Icon, InrPipe, SmartImage, VegMark, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><div><span class="eyebrow">Catalogue</span><h1>Menu items</h1></div>
      <button class="btn btn-primary" (click)="edit(null)"><bb-icon name="plus" [size]="18" /> New dish</button></div>

    <div class="toolbar">
      <input class="input grow" [(ngModel)]="search" (keyup.enter)="page = 1; load()" placeholder="Search dishes or tags" aria-label="Search dishes" />
      <select class="input" [(ngModel)]="categoryId" (ngModelChange)="page = 1; load()" aria-label="Category">
        <option [ngValue]="undefined">All categories</option>
        @for (c of categories(); track c.id) { <option [ngValue]="c.id">{{ c.name }}</option> }
      </select>
      <label class="check"><input type="checkbox" [(ngModel)]="lowOnly" (ngModelChange)="page = 1; load()" /> Low stock only</label>
    </div>

    <div class="card">
      @if (result(); as r) {
        @if (r.items.length) {
          <div class="table-wrap"><table class="table">
            <thead><tr><th></th><th>Dish</th><th>Category</th><th class="num">Price</th><th class="num">Stock</th><th>Status</th><th></th></tr></thead>
            <tbody>@for (p of r.items; track p.id) {
              <tr>
                <td><bb-img class="thumb" [src]="p.imageUrl" [alt]="p.name" /></td>
                <td><div class="row"><bb-veg [veg]="p.isVegetarian" /><strong>{{ p.name }}</strong>@if (p.isFeatured) {<span class="badge badge-brand">Featured</span>}</div>
                  <span class="muted small">★ {{ p.averageRating }} ({{ p.reviewCount }})</span></td>
                <td>{{ p.categoryName }}</td>
                <td class="num">{{ p.price | inr }}</td>
                <td class="num"><span [class.low]="p.isLowStock">{{ p.stockQuantity }}</span></td>
                <td>@if (p.isAvailable) {<span class="badge badge-success">On menu</span>} @else {<span class="badge">Hidden</span>}</td>
                <td><div class="actions-cell">
                  <button class="btn btn-sm" (click)="edit(p)"><bb-icon name="edit" [size]="15" /> Edit</button>
                  <button class="btn btn-sm btn-danger" (click)="remove(p)" [attr.aria-label]="'Remove ' + p.name"><bb-icon name="trash" [size]="15" /></button>
                </div></td>
              </tr>
            }</tbody></table></div>
          <div class="pager"><span class="muted small">{{ r.totalCount }} dishes</span>
            <button class="btn btn-sm" [disabled]="r.page <= 1" (click)="page = r.page - 1; load()">Prev</button>
            <span class="small">{{ r.page }} / {{ r.totalPages || 1 }}</span>
            <button class="btn btn-sm" [disabled]="r.page >= r.totalPages" (click)="page = r.page + 1; load()">Next</button></div>
        } @else { <bb-empty icon="chef" title="No dishes match" /> }
      } @else { <div class="skeleton" style="height:300px"></div> }
    </div>

    @if (editing() !== undefined) {
      <div class="modal-scrim" (click)="editing.set(undefined)">
        <form class="modal" [formGroup]="form" (ngSubmit)="save()" (click)="$event.stopPropagation()" novalidate>
          <h2>{{ editing() ? 'Edit ' + editing()!.name : 'New dish' }}</h2>
          <div class="form-grid">
            <div class="field full"><label for="pf-name">Name</label><input id="pf-name" class="input" formControlName="name" />
              @if (err('name')) {<span class="field-error">{{ err('name') }}</span>}</div>
            <div class="field full"><label for="pf-desc">Description</label><textarea id="pf-desc" class="input" formControlName="description"></textarea>
              @if (err('description')) {<span class="field-error">{{ err('description') }}</span>}</div>
            <div class="field"><label for="pf-cat">Category</label>
              <select id="pf-cat" class="input" formControlName="categoryId">@for (c of categories(); track c.id) {<option [ngValue]="c.id">{{ c.name }}</option>}</select></div>
            <div class="field"><label for="pf-price">Price (₹)</label><input id="pf-price" class="input" type="number" min="1" step="1" formControlName="price" />
              @if (err('price')) {<span class="field-error">{{ err('price') }}</span>}</div>
            <div class="field"><label for="pf-spice">Spice level</label>
              <select id="pf-spice" class="input" formControlName="spiceLevel">@for (s of spices; track s) {<option [value]="s">{{ s }}</option>}</select></div>
            <div class="field"><label for="pf-prep">Prep time (min)</label><input id="pf-prep" class="input" type="number" formControlName="prepTimeMinutes" /></div>
            <div class="field"><label for="pf-serves">Serves</label><input id="pf-serves" class="input" type="number" formControlName="servesCount" /></div>
            <div class="field"><label for="pf-kcal">Calories (optional)</label><input id="pf-kcal" class="input" type="number" formControlName="calories" /></div>
            <div class="field"><label for="pf-low">Low-stock alert at</label><input id="pf-low" class="input" type="number" formControlName="lowStockThreshold" /></div>
            @if (!editing()) {
              <div class="field"><label for="pf-stock">Opening stock</label><input id="pf-stock" class="input" type="number" formControlName="initialStock" /></div>
            } @else { <div class="field"><label>Stock</label><p class="small muted" style="margin:10px 0 0">{{ editing()!.stockQuantity }} portions. Change it from Inventory.</p></div> }
            <div class="field full"><label for="pf-tags">Tags (comma-separated, used by search & BiteBot)</label><input id="pf-tags" class="input" formControlName="tags" placeholder="healthy, protein, grilled" /></div>
            <div class="field full"><label for="pf-img">Image URL (https) or upload after saving</label><input id="pf-img" class="input" formControlName="imageUrl" />
              @if (err('imageUrl')) {<span class="field-error">{{ err('imageUrl') }}</span>}</div>
            <div class="row full">
              <label class="check"><input type="checkbox" formControlName="isVegetarian" /> Vegetarian</label>
              <label class="check"><input type="checkbox" formControlName="isAvailable" /> Show on menu</label>
              <label class="check"><input type="checkbox" formControlName="isFeatured" /> Featured</label>
            </div>
            @if (editing()) {
              <div class="field full upload"><label>Upload photo (JPG/PNG/WEBP, max 2 MB)</label>
                <input type="file" accept="image/jpeg,image/png,image/webp" (change)="upload($event)" [disabled]="busy()" /></div>
            }
          </div>
          <div class="row" style="margin-top:var(--s5)"><button class="btn btn-primary" [disabled]="busy()">Save</button>
            <button class="btn btn-ghost" type="button" (click)="editing.set(undefined)">Cancel</button></div>
        </form>
      </div>
    }`,
  styles: [ADMIN_PAGE, `.low { color: var(--bb-danger); font-weight: 800; } .row { flex-wrap: wrap; } .upload input { font: inherit; }`],
})
export class AdminProductsPage {
  private readonly api = inject(AdminApi);
  private readonly toast = inject(ToastService);
  protected readonly spices: SpiceLevel[] = ['None', 'Mild', 'Medium', 'Hot'];
  protected readonly categories = signal<AdminCategory[]>([]);
  protected readonly result = signal<PagedResult<AdminProduct> | null>(null);
  protected readonly editing = signal<AdminProduct | null | undefined>(undefined);
  protected readonly busy = signal(false);
  private readonly serverErrors = signal<Record<string, string>>({});
  protected search = '';
  protected categoryId: number | undefined;
  protected lowOnly = false;
  protected page = 1;

  protected readonly form = inject(FormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: ['', [Validators.required, Validators.maxLength(600)]],
    categoryId: [0, [Validators.required, Validators.min(1)]],
    price: [0, [Validators.required, Validators.min(1)]],
    imageUrl: [''],
    isVegetarian: [true],
    spiceLevel: ['Mild' as SpiceLevel],
    calories: [null as number | null],
    prepTimeMinutes: [15, [Validators.required, Validators.min(1)]],
    servesCount: [1, [Validators.required, Validators.min(1)]],
    tags: [''],
    isAvailable: [true],
    isFeatured: [false],
    lowStockThreshold: [10],
    initialStock: [50 as number | null],
  });

  constructor() {
    this.api.categories().subscribe({ next: c => this.categories.set(c), error: e => this.toast.error(e) });
    this.load();
  }

  protected load(): void {
    this.api.products({ search: this.search.trim() || undefined, categoryId: this.categoryId, lowStockOnly: this.lowOnly || undefined, page: this.page, pageSize: 20 })
      .subscribe({ next: r => this.result.set(r), error: e => this.toast.error(e) });
  }

  protected err(name: string): string | null {
    const server = this.serverErrors()[name];
    if (server) return server;
    const c = this.form.get(name);
    return c && c.touched && c.invalid ? 'Please check this field.' : null;
  }

  protected edit(p: AdminProduct | null): void {
    this.serverErrors.set({});
    this.form.reset({
      name: p?.name ?? '', description: p?.description ?? '', categoryId: p?.categoryId ?? this.categories()[0]?.id ?? 0,
      price: p?.price ?? 199, imageUrl: p?.imageUrl ?? '', isVegetarian: p?.isVegetarian ?? true, spiceLevel: p?.spiceLevel ?? 'Mild',
      calories: p?.calories ?? null, prepTimeMinutes: p?.prepTimeMinutes ?? 15, servesCount: p?.servesCount ?? 1, tags: p?.tags ?? '',
      isAvailable: p?.isAvailable ?? true, isFeatured: p?.isFeatured ?? false, lowStockThreshold: p?.lowStockThreshold ?? 10, initialStock: p ? null : 50,
    });
    this.editing.set(p);
  }

  protected save(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    const body = {
      name: v.name!, description: v.description!, categoryId: Number(v.categoryId), price: Number(v.price), imageUrl: v.imageUrl || null,
      isVegetarian: !!v.isVegetarian, spiceLevel: v.spiceLevel!, calories: v.calories ? Number(v.calories) : null,
      prepTimeMinutes: Number(v.prepTimeMinutes), servesCount: Number(v.servesCount), tags: v.tags || null,
      isAvailable: !!v.isAvailable, isFeatured: !!v.isFeatured, lowStockThreshold: Number(v.lowStockThreshold),
      initialStock: v.initialStock === null ? null : Number(v.initialStock),
    };
    this.busy.set(true);
    const existing = this.editing();
    (existing ? this.api.updateProduct(existing.id, body) : this.api.createProduct(body)).subscribe({
      next: p => { this.busy.set(false); this.toast.success(`${p.name} saved`); this.editing.set(undefined); this.load(); },
      error: e => { this.busy.set(false); this.serverErrors.set(fieldErrors(e)); this.toast.error(e); },
    });
  }

  protected upload(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    const p = this.editing();
    if (!file || !p) return;
    if (file.size > 2 * 1024 * 1024) { this.toast.error('Image must be smaller than 2 MB.'); return; }
    this.busy.set(true);
    this.api.uploadImage(p.id, file).subscribe({
      next: updated => { this.busy.set(false); this.editing.set(updated); this.form.patchValue({ imageUrl: updated.imageUrl }); this.toast.success('Photo uploaded'); this.load(); },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }

  protected remove(p: AdminProduct): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.deleteProduct(p.id).subscribe({
      next: () => { this.busy.set(false); this.toast.success(`${p.name} removed from the menu (order history kept)`); this.load(); },
      error: e => { this.busy.set(false); this.toast.error(e); },
    });
  }
}

/* ============================== Categories ============================== */

@Component({
  selector: 'bb-admin-categories',
  imports: [FormsModule, Icon, SmartImage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><div><span class="eyebrow">Catalogue</span><h1>Categories</h1></div>
      <button class="btn btn-primary" (click)="startNew()"><bb-icon name="plus" [size]="18" /> New category</button></div>
    @if (draft(); as d) {
      <form class="card card-pad form-grid" style="margin-bottom:var(--s4)" (submit)="$event.preventDefault(); save()">
        <div class="field"><label for="c-name">Name</label><input id="c-name" class="input" name="name" [(ngModel)]="d.name" required maxlength="60" /></div>
        <div class="field"><label for="c-order">Display order</label><input id="c-order" class="input" type="number" name="order" [(ngModel)]="d.displayOrder" /></div>
        <div class="field full"><label for="c-desc">Description</label><input id="c-desc" class="input" name="desc" [(ngModel)]="d.description" maxlength="300" /></div>
        <div class="field full"><label for="c-img">Image URL</label><input id="c-img" class="input" name="img" [(ngModel)]="d.imageUrl" /></div>
        <label class="check"><input type="checkbox" name="active" [(ngModel)]="d.isActive" /> Visible on storefront</label>
        <div class="row full"><button class="btn btn-primary" [disabled]="!d.name.trim()">Save</button><button class="btn btn-ghost" type="button" (click)="draft.set(null)">Cancel</button></div>
      </form>
    }
    <div class="card"><div class="table-wrap"><table class="table">
      <thead><tr><th></th><th>Name</th><th>Slug</th><th class="num">Dishes</th><th class="num">Order</th><th>Visible</th><th></th></tr></thead>
      <tbody>@for (c of items(); track c.id) {
        <tr><td><bb-img class="thumb" [src]="c.imageUrl" [alt]="c.name" /></td><td><strong>{{ c.name }}</strong><br /><span class="muted small">{{ c.description }}</span></td>
          <td class="muted">{{ c.slug }}</td><td class="num">{{ c.productCount }}</td><td class="num">{{ c.displayOrder }}</td>
          <td>@if (c.isActive) {<span class="badge badge-success">Yes</span>} @else {<span class="badge">Hidden</span>}</td>
          <td><div class="actions-cell"><button class="btn btn-sm" (click)="edit(c)">Edit</button>
            <button class="btn btn-sm btn-danger" (click)="remove(c)" [disabled]="c.productCount > 0" [attr.title]="c.productCount ? 'Move or remove its dishes first' : null">Delete</button></div></td></tr>
      }</tbody></table></div></div>`,
  styles: [ADMIN_PAGE],
})
export class AdminCategoriesPage {
  private readonly api = inject(AdminApi);
  private readonly toast = inject(ToastService);
  protected readonly items = signal<AdminCategory[]>([]);
  protected readonly draft = signal<(Omit<AdminCategory, 'slug' | 'productCount' | 'description' | 'imageUrl'> & { description: string; imageUrl: string }) | null>(null);

  constructor() { this.load(); }
  protected load(): void { this.api.categories().subscribe({ next: c => this.items.set(c), error: e => this.toast.error(e) }); }
  protected startNew(): void { this.draft.set({ id: 0, name: '', description: '', imageUrl: '', displayOrder: this.items().length + 1, isActive: true }); }
  protected edit(c: AdminCategory): void { this.draft.set({ ...c, description: c.description ?? '', imageUrl: c.imageUrl ?? '' }); }

  protected save(): void {
    const d = this.draft()!;
    const body = { name: d.name, description: d.description || null, imageUrl: d.imageUrl || null, displayOrder: Number(d.displayOrder), isActive: d.isActive };
    (d.id ? this.api.updateCategory(d.id, body) : this.api.createCategory(body)).subscribe({
      next: () => { this.toast.success('Category saved'); this.draft.set(null); this.load(); },
      error: e => this.toast.error(e),
    });
  }

  protected remove(c: AdminCategory): void {
    this.api.deleteCategory(c.id).subscribe({ next: () => { this.toast.success('Category deleted'); this.load(); }, error: e => this.toast.error(e) });
  }
}

/* ============================== Inventory ============================== */

@Component({
  selector: 'bb-admin-inventory',
  imports: [FormsModule, DatePipe, Icon, SmartImage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><div><span class="eyebrow">Kitchen stock</span><h1>Inventory</h1></div>
      <label class="check"><input type="checkbox" [(ngModel)]="lowOnly" (ngModelChange)="load()" /> Low stock only</label></div>
    <div class="grid">
      <div class="card"><div class="table-wrap"><table class="table">
        <thead><tr><th></th><th>Dish</th><th class="num">In stock</th><th class="num">Alert at</th><th>Adjust</th></tr></thead>
        <tbody>@for (i of items(); track i.productId) {
          <tr [class.warn]="i.isLowStock">
            <td><bb-img class="thumb" [src]="i.imageUrl" [alt]="i.name" /></td>
            <td><strong>{{ i.name }}</strong><br /><span class="muted small">{{ i.categoryName }}</span></td>
            <td class="num"><strong [class.danger]="i.stockQuantity === 0">{{ i.stockQuantity }}</strong>@if (i.isLowStock) {<br /><span class="badge badge-warning">Low</span>}</td>
            <td class="num">{{ i.lowStockThreshold }}</td>
            <td><div class="adjust">
              <input class="input" type="number" [(ngModel)]="deltas[i.productId]" placeholder="+20 / -5" [attr.aria-label]="'Quantity change for ' + i.name" />
              <select class="input" [(ngModel)]="reasons[i.productId]" aria-label="Reason">
                <option value="Restock">Restock</option><option value="Wastage">Wastage</option><option value="ManualAdjustment">Correction</option>
              </select>
              <button class="btn btn-sm btn-dark" (click)="adjust(i)" [disabled]="!deltas[i.productId]">Apply</button>
              <button class="btn btn-sm btn-ghost" (click)="history(i.productId)" aria-label="View history"><bb-icon name="clock" [size]="16" /></button>
            </div></td>
          </tr>
        }</tbody></table></div></div>
      <aside class="card card-pad">
        <div class="row"><h3 style="margin:0;font-family:var(--bb-font);font-size:1rem">Stock movements</h3><span class="spacer"></span>
          @if (historyFor()) {<button class="btn btn-sm btn-ghost" (click)="history(undefined)">Show all</button>}</div>
        <ul class="moves">
          @for (t of moves()?.items ?? []; track t.id) {
            <li><span class="delta" [class.neg]="t.quantityChange < 0">{{ t.quantityChange > 0 ? '+' : '' }}{{ t.quantityChange }}</span>
              <div><strong>{{ t.productName }}</strong><span class="muted small">{{ reasonLabel(t.reason) }}@if (t.orderNumber) { · {{ t.orderNumber }}}@if (t.note && !t.orderNumber) { · {{ t.note }}} · {{ utc(t.createdAt) | date: 'd MMM, h:mm a' }}</span></div>
              <span class="muted small">→ {{ t.stockAfter }}</span></li>
          } @empty { <li class="muted small">No movements yet.</li> }
        </ul>
      </aside>
    </div>`,
  styles: [ADMIN_PAGE, `
    .grid { display: grid; grid-template-columns: minmax(0, 1.6fr) minmax(280px, 1fr); gap: var(--s4); align-items: start; }
    .warn { background: #fffaf0; }
    .danger { color: var(--bb-danger); }
    .adjust { display: flex; gap: 6px; align-items: center; }
    .adjust .input { min-height: 36px; padding: 4px 8px; }
    .adjust input { width: 90px; } .adjust select { width: 120px; }
    .moves { list-style: none; padding: 0; margin: var(--s3) 0 0; display: flex; flex-direction: column; gap: 10px; max-height: 70vh; overflow: auto; }
    .moves li { display: flex; gap: 10px; align-items: center; font-size: .86rem; }
    .moves div { display: flex; flex-direction: column; flex: 1; min-width: 0; }
    .delta { min-width: 46px; text-align: center; font-weight: 800; padding: 3px 6px; border-radius: 8px; background: var(--bb-success-soft); color: var(--bb-success); }
    .delta.neg { background: var(--bb-danger-soft); color: var(--bb-danger); }
    @media (max-width: 1100px) { .grid { grid-template-columns: 1fr; } }`],
})
export class AdminInventoryPage {
  private readonly api = inject(AdminApi);
  private readonly toast = inject(ToastService);
  protected readonly utc = utc;
  protected readonly items = signal<InventoryItem[]>([]);
  protected readonly moves = signal<PagedResult<InventoryTransaction> | null>(null);
  protected readonly historyFor = signal<number | undefined>(undefined);
  protected lowOnly = false;
  protected deltas: Record<number, number | null> = {};
  protected reasons: Record<number, InventoryReason> = {};

  constructor() { this.load(); this.history(undefined); }

  protected load(): void { this.api.inventory(this.lowOnly).subscribe({ next: i => this.items.set(i), error: e => this.toast.error(e) }); }

  protected history(productId: number | undefined): void {
    this.historyFor.set(productId);
    this.api.inventoryHistory(productId).subscribe({ next: m => this.moves.set(m), error: e => this.toast.error(e) });
  }

  protected reasonLabel(r: InventoryReason): string {
    return ({ InitialStock: 'Opening stock', Restock: 'Restock', OrderPlaced: 'Order', OrderCancelled: 'Order cancelled', ManualAdjustment: 'Correction', Wastage: 'Wastage' })[r];
  }

  protected adjust(i: InventoryItem): void {
    const delta = Number(this.deltas[i.productId]);
    if (!delta) return;
    this.api.adjustStock(i.productId, { quantityChange: delta, reason: this.reasons[i.productId] ?? 'Restock', note: null }).subscribe({
      next: updated => {
        this.items.update(list => list.map(x => (x.productId === updated.productId ? updated : x)));
        this.deltas[i.productId] = null;
        this.toast.success(`${i.name}: stock is now ${updated.stockQuantity}`);
        this.history(this.historyFor());
      },
      error: e => this.toast.error(e),
    });
  }
}
