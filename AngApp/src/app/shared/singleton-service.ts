import { HttpClient } from '@angular/common/http';
import { inject, Service, signal } from '@angular/core';
import { WindowService } from './window-service';

@Service()
export class SingletonService {
    private readonly httpClient = inject(HttpClient);
    private windowService = inject(WindowService);

    authChekced = signal(false);
    darkMode = signal(false);

    constructor(){
        let theme = localStorage.getItem("theme");
        if(theme && theme == "dark"){
        this.darkMode.set(true);
        this.windowService.nativeWindow.document.body.classList.add('dark-mode');
        }
        else{
        this.darkMode.set(false);
        this.windowService.nativeWindow.document.body.classList.remove('dark-mode');
        }
    }

}
