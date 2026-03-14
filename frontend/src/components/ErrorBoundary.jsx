import React from "react";
import { reportClientError } from "../utils/diagnostics";

export default class ErrorBoundary extends React.Component {
    constructor(props) {
        super(props);
        this.state = { hasError: false, error: null };
        this.resetErrorBoundary = this.resetErrorBoundary.bind(this);
    }

    static getDerivedStateFromError(error) {
        return { hasError: true, error };
    }

    componentDidCatch(error, errorInfo) {
        reportClientError({
            severity: 'error',
            source: 'react.errorboundary',
            message: error?.message || 'React rendering error',
            stack: error?.stack || null,
            componentStack: errorInfo?.componentStack || null,
            url: window.location.href
        });
    }

    resetErrorBoundary() {
        this.setState({ hasError: false, error: null });
    }

    render() {
        const { hasError, error } = this.state;
        const Fallback = this.props.fallback;

        if (hasError && Fallback) {
            return <Fallback error={error} resetErrorBoundary={this.resetErrorBoundary} />;
        }

        return this.props.children;
    }
}
