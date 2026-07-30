import { Service } from '@angular/core';

@Service()
export class WindowService {
    get nativeWindow(): Window {
    return window;
  }
}
