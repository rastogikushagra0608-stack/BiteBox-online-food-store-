import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AccountApi } from '../core/api/api';
import { Address, AddressRequest } from '../core/api/models';
import { AuthStore } from '../core/state/auth.store';
import { ToastService, fieldErrors } from '../core/state/toast';

/** Reusable add/edit address form (used at checkout and in the account area). */
@Component({
  selector: 'bb-address-form',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="save()" class="form-grid" novalidate>
      <div class="field"><label for="a-label">Label</label>
        <select id="a-label" class="input" formControlName="label"><option>Home</option><option>Work</option><option>Hostel</option><option>Other</option></select></div>
      <div class="field"><label for="a-name">Recipient name</label><input id="a-name" class="input" formControlName="recipientName" autocomplete="name" />
        @if (err('recipientName')) {<span class="field-error">{{ err('recipientName') }}</span>}</div>
      <div class="field full"><label for="a-l1">House / flat, street</label><input id="a-l1" class="input" formControlName="line1" autocomplete="address-line1" />
        @if (err('line1')) {<span class="field-error">{{ err('line1') }}</span>}</div>
      <div class="field full"><label for="a-l2">Area / locality (optional)</label><input id="a-l2" class="input" formControlName="line2" autocomplete="address-line2" /></div>
      <div class="field"><label for="a-city">City</label><input id="a-city" class="input" formControlName="city" autocomplete="address-level2" />
        @if (err('city')) {<span class="field-error">{{ err('city') }}</span>}</div>
      <div class="field"><label for="a-pin">Pincode</label><input id="a-pin" class="input" formControlName="pincode" inputmode="numeric" maxlength="6" autocomplete="postal-code" />
        @if (err('pincode')) {<span class="field-error">{{ err('pincode') }}</span>}</div>
      <div class="field"><label for="a-phone">Phone</label><input id="a-phone" class="input" formControlName="phone" inputmode="numeric" maxlength="10" autocomplete="tel-national" />
        @if (err('phone')) {<span class="field-error">{{ err('phone') }}</span>}</div>
      <div class="field"><label for="a-lm">Landmark (optional)</label><input id="a-lm" class="input" formControlName="landmark" /></div>
      <label class="check full"><input type="checkbox" formControlName="isDefault" /> Make this my default address</label>
      <div class="row full">
        <button class="btn btn-primary" type="submit" [disabled]="busy()">{{ address() ? 'Save changes' : 'Save address' }}</button>
        <button class="btn btn-ghost" type="button" (click)="cancelled.emit()">Cancel</button>
      </div>
    </form>`,
})
export class AddressForm {
  private readonly api = inject(AccountApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthStore);

  readonly address = input<Address | null>(null);
  readonly saved = output<Address>();
  readonly cancelled = output<void>();
  protected readonly busy = signal(false);
  private readonly serverErrors = signal<Record<string, string>>({});

  protected readonly form = inject(FormBuilder).nonNullable.group({
    label: ['Home', Validators.required],
    recipientName: [this.auth.user()?.fullName ?? '', [Validators.required, Validators.maxLength(80)]],
    phone: [this.auth.user()?.phone ?? '', [Validators.required, Validators.pattern(/^[6-9]\d{9}$/)]],
    line1: ['', [Validators.required, Validators.maxLength(150)]],
    line2: [''],
    city: ['Bengaluru', [Validators.required, Validators.maxLength(60)]],
    pincode: ['', [Validators.required, Validators.pattern(/^[1-9]\d{5}$/)]],
    landmark: [''],
    isDefault: [false],
  });

  constructor() {
    effect(() => {
      const a = this.address();
      if (a) this.form.patchValue({ ...a, line2: a.line2 ?? '', landmark: a.landmark ?? '' });
    });
  }

  protected err(name: keyof typeof this.form.controls): string | null {
    const server = this.serverErrors()[name];
    if (server) return server;
    const c = this.form.controls[name];
    if (!c.touched || c.valid) return null;
    if (name === 'pincode') return 'Enter a valid 6-digit pincode.';
    if (name === 'phone') return 'Enter a valid 10-digit mobile number.';
    return 'This field is required.';
  }

  protected save(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.busy.set(true);
    const v = this.form.getRawValue();
    const body: AddressRequest = { ...v, line2: v.line2 || null, landmark: v.landmark || null };
    const existing = this.address();
    (existing ? this.api.updateAddress(existing.id, body) : this.api.createAddress(body)).subscribe({
      next: a => { this.busy.set(false); this.toast.success('Address saved'); this.saved.emit(a); },
      error: e => { this.busy.set(false); this.serverErrors.set(fieldErrors(e)); this.toast.error(e); },
    });
  }
}
