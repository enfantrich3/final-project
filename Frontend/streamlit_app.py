import json
from datetime import date, timedelta

import requests
import streamlit as st
import urllib3

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

ROLE = {"guest": 0, "tenant": 1, "host": 2, "admin": 3}
PTYPE = {"apartment": 0, "house": 1, "room": 2, "studio": 3}
PSTATUS = {"draft": 0, "active": 1, "inactive": 2, "blocked": 3}

st.set_page_config(page_title="RentalService", page_icon="🏠", layout="centered")

if "base_url" not in st.session_state:
    st.session_state.base_url = "http://localhost:5121"
if "token" not in st.session_state:
    st.session_state.token = ""


def api(method, path, body=None, params=None, auth=True):
    url = st.session_state.base_url.rstrip("/") + path
    headers = {}
    if auth and st.session_state.token:
        headers["Authorization"] = f"Bearer {st.session_state.token}"
    if params:
        params = {k: v for k, v in params.items() if v not in (None, "", 0)}
    try:
        r = requests.request(method, url, json=body, params=params, headers=headers, verify=False, timeout=20)
    except requests.RequestException as e:
        st.error(f"Нет связи с API: {e}")
        return None
    try:
        data = r.json()
    except ValueError:
        data = r.text
    if r.ok:
        st.success(f"HTTP {r.status_code}")
    else:
        st.error(f"HTTP {r.status_code}: {data}")
    st.code(json.dumps(data, ensure_ascii=False, indent=2, default=str), language="json")
    return data if r.ok else None


def load_reviews(property_id):
    url = st.session_state.base_url.rstrip("/") + f"/api/v1/reviews/property/{property_id}"
    r = requests.get(url)
    return r.json()


with st.sidebar:
    st.session_state.base_url = st.text_input("Адрес backend", st.session_state.base_url)
    if st.session_state.token:
        st.success("Вход выполнен")
        if st.button("Выйти"):
            st.session_state.token = ""
            st.rerun()
    else:
        st.warning("Не выполнен вход")

st.title("Сервис посуточной аренды")

tabs = st.tabs(["Вход", "Профиль", "Каталог", "Объекты", "Брони", "Админ"])

with tabs[0]:
    st.subheader("Регистрация")
    email = st.text_input("Email", "tenant@demo.ru", key="r_email")
    pwd = st.text_input("Пароль", "123456", type="password", key="r_pwd")
    name = st.text_input("Имя", "Иван", key="r_name")
    role = st.selectbox("Роль", list(ROLE), index=1, key="r_role")
    if st.button("Зарегистрироваться"):
        data = api("POST", "/register", {"email": email, "password": pwd, "name": name, "role": ROLE[role]}, auth=False)
        if data and data.get("token"):
            st.session_state.token = data["token"]

    st.divider()
    st.subheader("Вход")
    le = st.text_input("Email", "tenant@demo.ru", key="l_email")
    lp = st.text_input("Пароль", "123456", type="password", key="l_pwd")
    if st.button("Войти"):
        data = api("POST", "/login", {"email": le, "password": lp}, auth=False)
        if data and data.get("token"):
            st.session_state.token = data["token"]
            st.rerun()

with tabs[1]:
    if not st.session_state.token:
        st.info("Сначала войдите.")
    else:
        if st.button("Показать профиль"):
            api("GET", "/profile")
        if st.button("Верификация личности"):
            api("POST", "/verify-identity")

with tabs[2]:
    city = st.text_input("Город", key="c_city")
    if st.button("Найти объекты"):
        st.session_state.catalog = api("GET", "/api/v1/properties", params={"city": city}, auth=False) or []
    for p in st.session_state.get("catalog", []):
        st.write(f"**{p.get('title')}** — {p.get('city')}, {p.get('basePrice')} ₽/сутки  \n`{p.get('id')}`")
    st.divider()
    st.subheader("Отзывы")
    rpid = st.text_input("ID объекта", key="rv_pid")
    if st.button("Показать отзывы"):
        for rv in load_reviews(rpid):
            st.write(f"{'★' * rv['rating']} — {rv['text']}")
    st.divider()
    st.subheader("Создать бронь")
    if not st.session_state.token:
        st.info("Для брони нужно войти.")
    else:
        pid = st.text_input("ID объекта", key="b_pid")
        cin = st.date_input("Заезд", date.today() + timedelta(days=7), key="b_in")
        cout = st.date_input("Выезд", date.today() + timedelta(days=10), key="b_out")
        if st.button("Забронировать"):
            if cin >= cout:
                st.error("Дата заезда должна быть раньше выезда.")
            else:
                api("POST", "/api/v1/bookings", {"propertyId": pid, "checkIn": cin.isoformat(), "checkOut": cout.isoformat()})

with tabs[3]:
    if not st.session_state.token:
        st.info("Войдите под ролью host.")
    else:
        st.subheader("Создать объект")
        t = st.text_input("Название", "Квартира у метро", key="o_title")
        ct = st.text_input("Город", "Санкт-Петербург", key="o_city")
        ad = st.text_input("Адрес", "Невский проспект, 1", key="o_addr")
        tp = st.selectbox("Тип", list(PTYPE), key="o_type")
        rooms = st.number_input("Комнат", 1, value=1, key="o_rooms")
        price = st.number_input("Цена за сутки", 1, value=4500, key="o_price")
        if st.button("Создать объект"):
            st.session_state.new_pid = (api("POST", "/api/v1/properties", {
                "title": t, "city": ct, "address": ad,
                "propertyType": PTYPE[tp], "rooms": int(rooms),
                "basePrice": float(price), "description": ""}) or {}).get("id")

        st.divider()
        st.subheader("Фото и публикация")
        pid2 = st.text_input("ID объекта", st.session_state.get("new_pid", "") or "", key="o_pid")
        url = st.text_input("URL фото", "https://images.unsplash.com/photo-1522708323590-d24dbb6b0267", key="o_photo")
        if st.button("Добавить фото"):
            api("POST", f"/api/v1/properties/{pid2}/photos", {"url": url, "isCover": True})
        if st.button("Опубликовать (active)"):
            api("PUT", f"/api/v1/properties/{pid2}/status", {"status": PSTATUS["active"]})

        st.divider()
        if st.button("Мои объекты"):
            api("GET", "/api/v1/properties/my")

with tabs[4]:
    if not st.session_state.token:
        st.info("Сначала войдите.")
    else:
        if st.button("Мои поездки"):
            api("GET", "/api/v1/bookings/trips")
        if st.button("Заказы на мои объекты"):
            api("GET", "/api/v1/bookings/orders")
        st.divider()
        bid = st.text_input("ID брони", key="p_bid")
        if st.button("Оплатить"):
            api("POST", "/api/v1/payments/process", {"bookingId": bid})
        if st.button("Отменить бронь"):
            api("POST", f"/api/v1/bookings/{bid}/cancel")

with tabs[5]:
    if not st.session_state.token:
        st.info("Войдите под admin.")
    else:
        if st.button("Список пользователей"):
            api("GET", "/admin/users")
        uid = st.text_input("ID пользователя", key="a_uid")
        if st.button("Верифицировать"):
            api("POST", f"/admin/users/{uid}/verify")
        if st.button("Заблокировать"):
            api("POST", f"/admin/users/{uid}/block")
