import "./styles.css";
import { StreamVueTvApp } from "./ui/StreamVueTvApp.js";

const root = document.querySelector<HTMLElement>("#app");
if (!root) throw new Error("StreamVue could not create its television surface.");

const app = new StreamVueTvApp(root);
void app.start();
