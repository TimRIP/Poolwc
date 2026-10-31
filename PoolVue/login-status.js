/* Shared account label. Never infer identity from JWT subject (it is constant in this API). */
(function () {
  function renderStatus() {
    var node = document.getElementById('pool-login-status');
    if (!node) return;
    var token = localStorage.getItem('token');
    var username = localStorage.getItem('poolLoggedInUsername');
    node.textContent = '';
    var status = document.createElement('span');
    status.className = 'statusText' + (token ? ' isLoggedIn' : '');
    status.textContent = !token ? 'Not logged in' : username ? 'Logged in as: ' + username : 'Logged in — sign in again to show username';
    node.appendChild(status);
  }
  window.addEventListener('storage', renderStatus);
  window.addEventListener('pool-login-changed', renderStatus);
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', renderStatus);
  else renderStatus();
}());
