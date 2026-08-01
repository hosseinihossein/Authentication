import { Routes } from '@angular/router';
import { Login } from './login/login';

export const routes: Routes = [
    {path: "Authorization/Api/Authorize", redirectTo: (activatedRouteSnapshot)=>{
            let url = new URL("https://localhost:5443/Authorization/Api/Authorize");
            for(let key of activatedRouteSnapshot.queryParamMap.keys){
                url.searchParams.append(key, activatedRouteSnapshot.queryParamMap.get(key)!);
            }
            window.location.href = url.toString();
            return "";
        }
    },
    {path: "", component: Login},
];
