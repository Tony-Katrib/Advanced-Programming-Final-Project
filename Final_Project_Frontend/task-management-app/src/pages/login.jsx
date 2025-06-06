import react, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import '../styles/login.css';
import { Link } from 'react-router-dom';
import Button from '../components/Button';
import loginImg from '../assets/login-img.jpg';

function Login() {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState('');
    const navigate = useNavigate();

    const handleLogin = async (e) => {
        e.preventDefault();
        setError('');
        const credentials = { email, password };

        try {
            const response = await fetch('http://localhost:5137/api/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(credentials),
            });

            let data;
            try {
                data = await response.json();
            } catch {
                const text = await response.text();
                throw new Error(text);
            }

            if (!response.ok) {
                setError(data || 'Login failed');
                return;
            }

            localStorage.setItem('token', data.token);
            localStorage.setItem('userFullName', data.fullName);
            localStorage.setItem('userEmail', email);

            navigate('/dashboard');
        } catch (err) {
            console.error('Login failed:', err);
            setError('Something went wrong. Please try again.');
        }
    };

    return (
        <section className='login-page'>
            <div className='login-div'>
                <div className='logo'>
                    <Link to="/" className='logo'>
                        Taskly
                    </Link>
                </div>
                <div className='login-header'>
                    <h2>Login</h2>
                    <p>Welcome Back! Login to access your dashboard!</p>
                </div>
                <form onSubmit={handleLogin} className='login-form'>
                    <div className='input-field'>
                        <label htmlFor='email'>Email</label>
                        <input
                            id='email'
                            type="text"
                            placeholder="user@example.com"
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                        />
                    </div>
                    <div className='input-field'>
                        <label htmlFor='password'>Password</label>
                        <input
                            id='password'
                            type="password"
                            placeholder="Enter your password"
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                        />
                    </div>

                    <Button text={"Log-in"} color='primary' />
                </form>
                {error && <p style={{ color: 'red' }}>{error}</p>}
            </div>
            <div className='img-side'>
                <img src={loginImg} alt="Side Image" />
            </div>
        </section>
    );
}

export default Login;