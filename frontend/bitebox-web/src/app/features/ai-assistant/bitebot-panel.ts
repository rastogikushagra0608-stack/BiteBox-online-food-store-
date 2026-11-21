import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BiteBotApi } from '../../core/api/api';
import { BiteBotResponse } from '../../core/api/models';
import { CartStore } from '../../core/state/cart.store';
import { problemMessage } from '../../core/state/toast';
import { Icon } from '../../shared/icon';
import { InrPipe, SmartImage, VegMark } from '../../shared/ui';

interface ChatTurn { role: 'user' | 'bot'; text: string; data?: BiteBotResponse; error?: boolean; }

const STARTERS = [
  'Spicy veg meal under ₹250',
  'Vegetarian dinner for two under ₹700',
  'Something healthy and light',
  'Similar to my last order',
];

/**
 * BiteBot chat panel. Every dish shown here comes from the API's catalogue response, with real prices
 * and stock; the assistant cannot invent items. It also never orders anything by itself: the user decides
 * what to add to the cart.
 */
@Component({
  selector: 'bb-bitebot',
  imports: [FormsModule, RouterLink, Icon, InrPipe, SmartImage, VegMark],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!open()) {
      <button class="launcher" type="button" (click)="open.set(true)" aria-label="Ask BiteBot for food suggestions">
        <bb-icon name="sparkles" [size]="22" /> <span>Ask BiteBot</span>
      </button>
    } @else {
      <section class="panel fade-up" role="dialog" aria-label="BiteBot food assistant">
        <header>
          <div class="bot-avatar"><bb-icon name="sparkles" [size]="20" /></div>
          <div><strong>BiteBot</strong><span class="small">Your food-finding sidekick</span></div>
          <button class="icon-btn" type="button" (click)="open.set(false)" aria-label="Close BiteBot"><bb-icon name="close" /></button>
        </header>

        <div class="log" #log>
          <div class="msg bot">
            Hi! Tell me what you're craving, your budget, or who you're feeding, and I'll suggest dishes from today's menu.
          </div>
          @for (turn of turns(); track $index) {
            <div class="msg" [class.user]="turn.role === 'user'" [class.bot]="turn.role === 'bot'" [class.error]="turn.error">{{ turn.text }}</div>
            @if (turn.data; as d) {
              @if (d.understoodAs.length) {
                <div class="chips"><span class="small muted">I understood:</span>@for (c of d.understoodAs; track c) {<span class="badge">{{ c }}</span>}</div>
              }
              <div class="sugs">
                @for (s of d.suggestions; track s.product.id) {
                  <div class="sug">
                    <a [routerLink]="['/menu', s.product.slug]" (click)="open.set(false)"><bb-img class="sug-img" [src]="s.product.imageUrl" [alt]="s.product.name" /></a>
                    <div class="sug-info">
                      <div class="row"><bb-veg [veg]="s.product.isVegetarian" /><a [routerLink]="['/menu', s.product.slug]" (click)="open.set(false)">{{ s.product.name }}</a></div>
                      <span class="small muted">{{ s.reason }}</span>
                    </div>
                    <div class="sug-end">
                      <strong>{{ s.product.price | inr }}</strong>
                      <button class="btn btn-sm" type="button" (click)="cart.add(s.product)" [disabled]="cart.busy()" [attr.aria-label]="'Add ' + s.product.name">Add</button>
                    </div>
                  </div>
                }
              </div>
              @if (d.combo; as combo) {
                <div class="combo">
                  <div class="row"><bb-icon name="bag" [size]="18" /><strong>Suggested combo</strong><span class="spacer"></span><strong>{{ combo.foodTotal | inr }}</strong></div>
                  <ul>@for (i of combo.items; track i.product.id) {<li>{{ i.quantity }} × {{ i.product.name }}</li>}</ul>
                  <p class="small muted">{{ combo.summary }}</p>
                  <button class="btn btn-primary btn-sm" type="button" (click)="addCombo(d)" [disabled]="cart.busy()">Add combo to cart</button>
                </div>
              }
              @if ($last && d.followUps.length) {
                <div class="chips">@for (f of d.followUps; track f) {<button class="chip" type="button" (click)="ask(f)">{{ f }}</button>}</div>
              }
            }
          }
          @if (loading()) { <div class="msg bot typing"><i></i><i></i><i></i></div> }
          @if (!turns().length) {
            <div class="chips">@for (s of starters; track s) {<button class="chip" type="button" (click)="ask(s)">{{ s }}</button>}</div>
          }
        </div>

        <form (submit)="$event.preventDefault(); ask(draft)">
          <input class="input" name="msg" [(ngModel)]="draft" maxlength="300" placeholder="e.g. paneer dinner under ₹300" autocomplete="off" aria-label="Message BiteBot" />
          <button class="btn btn-primary send" type="submit" [disabled]="loading() || !draft.trim()" aria-label="Send"><bb-icon name="send" [size]="18" /></button>
        </form>
        <p class="disclaimer">Suggestions come from our live menu. Please check allergens with the restaurant.</p>
      </section>
    }`,
  styles: `
    .launcher { position: fixed; right: 20px; bottom: 20px; z-index: 80; display: inline-flex; align-items: center; gap: 8px; height: 52px; padding: 0 20px; border-radius: 999px; border: 0; background: var(--bb-ink); color: #fff; font-weight: 700; cursor: pointer; box-shadow: var(--bb-shadow-lg); }
    .launcher bb-icon { color: #ffb38f; }
    .launcher:hover { background: #000; }
    .panel { position: fixed; right: 20px; bottom: 20px; z-index: 95; width: min(420px, calc(100vw - 24px)); height: min(640px, calc(100vh - 100px)); display: flex; flex-direction: column; background: var(--bb-bg); border: 1px solid var(--bb-border); border-radius: 20px; box-shadow: var(--bb-shadow-lg); overflow: hidden; }
    header { display: flex; align-items: center; gap: 10px; padding: 12px 12px 12px 16px; background: var(--bb-ink); color: #fff; }
    header div:nth-child(2) { display: flex; flex-direction: column; flex: 1; line-height: 1.2; }
    header .small { color: #c9bdb1; }
    header .icon-btn { color: #fff; }
    header .icon-btn:hover { background: rgba(255,255,255,.1); }
    .bot-avatar { width: 36px; height: 36px; border-radius: 50%; background: var(--bb-primary); display: grid; place-items: center; }
    .log { flex: 1; overflow-y: auto; padding: 16px; display: flex; flex-direction: column; gap: 10px; }
    .msg { max-width: 88%; padding: 10px 14px; border-radius: 16px; font-size: .92rem; line-height: 1.45; }
    .msg.bot { background: var(--bb-surface); border: 1px solid var(--bb-border); border-bottom-left-radius: 4px; }
    .msg.user { align-self: flex-end; background: var(--bb-primary); color: #fff; border-bottom-right-radius: 4px; }
    .msg.error { background: var(--bb-danger-soft); color: var(--bb-danger); border-color: transparent; }
    .chips { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
    .chips .chip { height: 32px; font-size: .8rem; }
    .sugs { display: flex; flex-direction: column; gap: 8px; }
    .sug { display: grid; grid-template-columns: 52px 1fr auto; gap: 10px; align-items: center; background: var(--bb-surface); border: 1px solid var(--bb-border); border-radius: 14px; padding: 8px; }
    .sug-img { width: 52px; height: 52px; border-radius: 10px; }
    .sug-info { min-width: 0; display: flex; flex-direction: column; gap: 2px; }
    .sug-info .row { gap: 6px; }
    .sug-info a { font-weight: 650; font-size: .88rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .sug-end { display: flex; flex-direction: column; align-items: flex-end; gap: 4px; }
    .sug-end .btn { min-height: 30px; padding: 0 12px; }
    .combo { background: var(--bb-primary-soft); border-radius: 14px; padding: 12px 14px; }
    .combo ul { margin: 6px 0; padding-left: 18px; font-size: .88rem; }
    .combo .row { gap: 8px; color: var(--bb-primary-ink); }
    .typing { display: inline-flex; gap: 4px; width: fit-content; }
    .typing i { width: 7px; height: 7px; border-radius: 50%; background: var(--bb-muted); animation: blink 1s infinite; }
    .typing i:nth-child(2) { animation-delay: .15s; } .typing i:nth-child(3) { animation-delay: .3s; }
    @keyframes blink { 50% { opacity: .25; } }
    form { display: flex; gap: 8px; padding: 12px; border-top: 1px solid var(--bb-border); background: var(--bb-surface); }
    form .input { border-radius: 999px; }
    .send { width: 44px; padding: 0; flex-shrink: 0; }
    .disclaimer { margin: 0; padding: 0 12px 10px; background: var(--bb-surface); font-size: .72rem; color: var(--bb-muted); text-align: center; }`,
})
export class BiteBotPanel {
  private readonly api = inject(BiteBotApi);
  protected readonly cart = inject(CartStore);
  private readonly log = viewChild<ElementRef<HTMLElement>>('log');

  protected readonly open = signal(false);
  protected readonly loading = signal(false);
  protected readonly turns = signal<ChatTurn[]>([]);
  protected readonly starters = STARTERS;
  protected draft = '';

  protected ask(message: string): void {
    const text = message.trim();
    if (!text || this.loading()) return;
    this.draft = '';
    this.turns.update(t => [...t, { role: 'user', text }]);
    this.loading.set(true);
    this.scroll();

    this.api.ask(text).subscribe({
      next: data => {
        this.turns.update(t => [...t, { role: 'bot', text: data.reply, data }]);
        this.loading.set(false);
        this.scroll();
      },
      error: err => {
        this.turns.update(t => [...t, { role: 'bot', text: problemMessage(err), error: true }]);
        this.loading.set(false);
        this.scroll();
      },
    });
  }

  protected async addCombo(d: BiteBotResponse): Promise<void> {
    for (const item of d.combo?.items ?? []) {
      if (!(await this.cart.add(item.product, item.quantity))) break;
    }
  }

  private scroll(): void {
    setTimeout(() => this.log()?.nativeElement.scrollTo({ top: 1e6, behavior: 'smooth' }));
  }
}
