import { Routes } from '@angular/router';
import { Login } from './login/login';

export const routes: Routes = [
    {path: "Api/Authorization/Authorize", redirectTo: (activatedRouteSnapshot)=>{
            let url = new URL("https://192.168.1.251/Api/Authorization/Authorize");
            for(let key of activatedRouteSnapshot.queryParamMap.keys){
                url.searchParams.append(key, activatedRouteSnapshot.queryParamMap.get(key)!);
            }
            window.location.href = url.toString();
            return "";
        }
    },
    {path: "", component: Login},
];
