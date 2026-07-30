import { inject, Service } from '@angular/core';
import { MatIconRegistry } from '@angular/material/icon';

@Service()
export class IconService {
  private matIconRegistry = inject(MatIconRegistry);

  constructor()
  {
    this.matIconRegistry.setDefaultFontSetClass("material-symbols-outlined");
  }

  fillIcon(){
    return "font-variation-settings:  'FILL' 1;";
  }
}
