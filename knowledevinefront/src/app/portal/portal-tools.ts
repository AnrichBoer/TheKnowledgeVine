import { CdkTrapFocus } from '@angular/cdk/a11y';
import { Component, inject } from '@angular/core';
import { PortalPwa } from './portal-pwa';

@Component({ selector: 'app-portal-tools', imports: [CdkTrapFocus], templateUrl: './portal-tools.html', styleUrl: './portal-tools.scss' })
export class PortalTools { readonly pwa = inject(PortalPwa); }
